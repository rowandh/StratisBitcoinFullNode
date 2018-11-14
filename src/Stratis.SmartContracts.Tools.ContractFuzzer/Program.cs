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

            sb.Append("using Stratis.SmartContracts;");
            sb.Append("[Deploy]");
            sb.Append($"public class F: SmartContract {{ public F(ISmartContractState state):base(state){{var s = new S();}}");
            sb.Append(CreateLargeStruct());
            sb.Append("}");

            //for (var i = 0; i < 100000; i++)
            //{

            //    sb.AppendLine(CreateType(NextRandom(rng, randoms)));

            //    //sb.AppendLine("public class Contract : SmartContract");
            //    //sb.AppendLine("{");
            //    //sb.AppendLine("public Contract(ISmartContractState state) : base(state)");
            //    //sb.AppendLine("{");
            //    //sb.AppendLine("}");
            //    //sb.AppendLine("}");

            //}

            using (var sw = new StreamWriter(@"C:\Users\Games\Desktop\LargeStruct.cs"))
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

        static string CreateType(int random)
        {
            return $"public class F{random}: SmartContract {{ public F{random}(ISmartContractState state) : base(state) {{}} }}";
        }

        static string CreateLargeStruct()
        {
            var sb = new StringBuilder();
            HashSet<int> randoms = new HashSet<int>();

            var rng = new Random();
            sb.Append("struct S{");

            while (Encoding.ASCII.GetBytes(sb.ToString()).Length < 50000)
            {
                sb.Append($"ulong F{NextRandom(rng, randoms)};");
            }

            sb.Append("}");

            return sb.ToString();
        }

    }
}
