using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Stratis.SmartContracts.Tools.ContractFuzzer
{
    class Program
    {
        static void Main(string[] args)
        {
            HashSet<int> randoms = new HashSet<int>();

            var rng = new Random();            

            var sb = new StringBuilder();

            sb.AppendLine("using Stratis.SmartContracts;");
            sb.AppendLine("public class Contract : SmartContract");
            sb.AppendLine("{");
            sb.AppendLine("public Contract(ISmartContractState state) : base(state)");
            sb.AppendLine("{");
            sb.AppendLine("}");

            for (var i = 0; i < 1000; i++)
            {
                sb.AppendLine(CreateField(NextRandom(rng, randoms)));
            }

            sb.AppendLine("}");

            using (var sw = new StreamWriter(@"C:\Users\Rowan\Desktop\1kFields.cs"))
            {
                sw.Write(sb.ToString());
            }
        }

        private static int NextRandom(Random rng, HashSet<int> randoms)
        {
            var random = rng.Next(int.MaxValue);

            while (randoms.Contains(random))
            {
                random = rng.Next(int.MaxValue);
            }

            randoms.Add(random);
            return random;
        }

        static string CreateField(int random)
        {
            return $"private string F{random};";
        }
    }
}
