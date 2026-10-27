# RAG 검색 평가 하니스
# eval.json 의 각 질문을 런북 의미검색에 넣어, 정답 id가 상위 몇 위에 나오는지 채점한다.
# 지표: Recall@1/@3/@5 (정답이 상위 K에 들어온 비율) + MRR (정답 순위의 역수 평균).
# 실행:  powershell -ExecutionPolicy Bypass -File eval_runner.ps1
# 필요:  런북 벡터(runbook.vec)가 있어야 함 → 앱에서 "전체 재임베딩" 먼저.

$ErrorActionPreference = 'Stop'
$core = 'D:\1. Source\STI.Guide\STI.Guide\STI.Copilot\bin\Debug\STI.Copilot.Core.dll'
[Reflection.Assembly]::LoadFrom($core) | Out-Null
$opt = New-Object STI.Copilot.Core.Ollama.OllamaOptions
$cli = New-Object STI.Copilot.Core.Ollama.OllamaClient($opt)
$svc = New-Object STI.Copilot.Core.Runbook.RunbookService($cli)
$svc.Load()

$evalPath = Join-Path $PSScriptRoot 'eval.json'
$raw  = [System.IO.File]::ReadAllText($evalPath, [System.Text.Encoding]::UTF8)
$data = $raw | ConvertFrom-Json
$K = 10
if ($data.searchK) { $K = [int]$data.searchK }

$titleMap = @{}
foreach ($e in $svc.Entries) { $titleMap[$e.Id] = $e.Title }

$n = 0; $h1 = 0; $h3 = 0; $h5 = 0; $mrr = 0.0
$rows = @()
foreach ($it in $data.items) {
    $q = $it.q
    $exp = @($it.expected)
    $t = $svc.SearchManyAsync($q, $K, [System.Threading.CancellationToken]::None)
    $t.Wait(120000) | Out-Null
    $hits = $t.Result

    $rank = 0
    for ($i = 0; $i -lt $hits.Count; $i++) {
        if ($exp -contains $hits[$i].Entry.Id) { $rank = $i + 1; break }
    }

    $n++
    if ($rank -eq 1)                 { $h1++ }
    if ($rank -ge 1 -and $rank -le 3){ $h3++ }
    if ($rank -ge 1 -and $rank -le 5){ $h5++ }
    if ($rank -ge 1)                 { $mrr += 1.0 / $rank }

    $top1 = '(none)'
    if ($hits.Count -gt 0) { $top1 = $hits[0].Entry.Id + ' ' + $titleMap[$hits[0].Entry.Id] }
    $rankStr = 'MISS'
    if ($rank -ge 1) { $rankStr = "$rank" }
    $rows += ("rank={0,-4} exp={1,-11} top1={2,-28} | q={3}" -f $rankStr, ($exp -join ','), $top1, $q)
}

$rec1 = [math]::Round(100.0 * $h1 / $n, 1)
$rec3 = [math]::Round(100.0 * $h3 / $n, 1)
$rec5 = [math]::Round(100.0 * $h5 / $n, 1)
$mrrv = [math]::Round($mrr / $n, 3)

$out = @()
$out += "=== RAG retrieval eval  (N=$n, searchK=$K) ==="
$out += ("Recall@1 = {0}%    Recall@3 = {1}%    Recall@5 = {2}%    MRR = {3}" -f $rec1, $rec3, $rec5, $mrrv)
$out += ""
$out += $rows
$nl = [char]13 + [char]10
[System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'eval_out.txt'), ($out -join $nl), [System.Text.Encoding]::UTF8)

Write-Output ("Recall@1={0}%  Recall@3={1}%  Recall@5={2}%  MRR={3}  (N={4})" -f $rec1, $rec3, $rec5, $mrrv, $n)
Write-Output "상세 결과 -> eval_out.txt"
