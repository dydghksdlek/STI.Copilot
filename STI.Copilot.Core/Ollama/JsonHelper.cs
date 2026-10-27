using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace STI.Copilot.Core.Ollama
{
    /// <summary>
    /// DataContractJsonSerializer 를 감싼 최소 JSON 도우미. 외부 NuGet 없이 동작.
    /// </summary>
    internal static class JsonHelper
    {
        public static string Serialize<T>(T obj)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(ms);
            }
        }
    }
}
