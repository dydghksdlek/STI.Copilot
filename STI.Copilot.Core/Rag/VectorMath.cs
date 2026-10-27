using System;

namespace STI.Copilot.Core.Rag
{
    /// <summary>벡터 연산 도우미. 임베딩은 정규화해 저장하므로 유사도는 내적(dot)으로 충분하다.</summary>
    internal static class VectorMath
    {
        /// <summary>L2 정규화(길이 1로). 정규화된 두 벡터의 내적 = 코사인 유사도.</summary>
        public static void NormalizeInPlace(float[] v)
        {
            if (v == null || v.Length == 0) return;
            double sum = 0;
            for (int i = 0; i < v.Length; i++) sum += (double)v[i] * v[i];
            double norm = Math.Sqrt(sum);
            if (norm <= 1e-12) return;
            for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
        }

        /// <summary>내적. 두 벡터가 정규화돼 있으면 코사인 유사도와 같다.</summary>
        public static float Dot(float[] a, float[] b)
        {
            if (a == null || b == null) return 0f;
            int len = Math.Min(a.Length, b.Length);
            float s = 0f;
            for (int i = 0; i < len; i++) s += a[i] * b[i];
            return s;
        }
    }
}
