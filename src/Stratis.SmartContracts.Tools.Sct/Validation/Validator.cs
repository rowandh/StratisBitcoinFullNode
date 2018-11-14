using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;
using Stratis.ModuleValidation.Net;
using Stratis.SmartContracts.Core.Validation;
using Stratis.SmartContracts.Executor.Reflection;
using Stratis.SmartContracts.Executor.Reflection.Compilation;
using Stratis.SmartContracts.Tools.Sct.Report;
using Stratis.SmartContracts.Tools.Sct.Report.Sections;

namespace Stratis.SmartContracts.Tools.Sct.Validation
{
    [Command(Description = "Validates smart contracts for structure and determinism")]
    [HelpOption]
    class Validator
    {
        [Argument(0, Description = "The paths of the files to validate",
            Name = "[FILES]")]
        public List<string> InputFiles { get; }

        [Option("-sb|--showbytes", CommandOptionType.NoValue,
            Description = "Show contract compilation bytes")]
        public bool ShowBytes { get; }

        [Option("-wb|--writebytes", CommandOptionType.NoValue, Description = "Write contract compilation bytes. The file written will be name [FILE].cil")]
        public bool WriteBytes { get; }

        private int OnExecute(CommandLineApplication app)
        {
            if (!this.InputFiles.Any())
            {
                app.ShowHelp();
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("Smart Contract Validator");
            Console.WriteLine();

            var determinismValidator = new SctDeterminismValidator();
            var formatValidator = new SmartContractFormatValidator();
            var warningValidator = new SmartContractWarningValidator();

            var reportData = new List<ValidationReportData>();

            foreach (string file in this.InputFiles)
            {
                if (!File.Exists(file))
                {
                    Console.WriteLine($"{file} does not exist");
                    continue;
                }

                string source;

                Console.WriteLine($"Reading {file}");

                using (var sr = new StreamReader(File.OpenRead(file)))
                {
                    source = sr.ReadToEnd();
                }

                Console.WriteLine($"Read {file} OK");
                Console.WriteLine();

                if (string.IsNullOrWhiteSpace(source))
                {
                    Console.WriteLine($"Empty file at {file}");
                    Console.WriteLine();
                    continue;
                }

                var validationData = new ValidationReportData
                {
                    FileName = file,
                    CompilationErrors = new List<CompilationError>(),
                    DeterminismValidationErrors = new List<ValidationResult>(),
                    FormatValidationErrors = new List<ValidationError>(),
                    Warnings = new List<Warning>()
                };

                reportData.Add(validationData);

                Console.WriteLine($"Compiling...");
                ContractCompilationResult compilationResult = ContractCompiler.Compile(source);

                validationData.CompilationSuccess = compilationResult.Success;

                if (!compilationResult.Success)
                {
                    Console.WriteLine("Compilation failed!");
                    Console.WriteLine();

                    validationData.CompilationErrors
                        .AddRange(compilationResult
                            .Diagnostics
                            .Select(d => new CompilationError { Message = d.ToString() }));

                    continue;
                }

                validationData.CompilationBytes = compilationResult.Compilation;

                Console.WriteLine($"Compilation OK");
                Console.WriteLine();

                byte[] compilation = compilationResult.Compilation;

                Console.WriteLine("Building ModuleDefinition");

                IContractModuleDefinition moduleDefinition = ContractDecompiler.GetModuleDefinition(compilation, new DotNetCoreAssemblyResolver()).Value;

                Console.WriteLine("ModuleDefinition built successfully");
                Console.WriteLine();

                Console.WriteLine($"Validating file {file}...");
                Console.WriteLine();

                SmartContractValidationResult formatValidationResult = formatValidator.Validate(moduleDefinition.ModuleDefinition);

                validationData.FormatValid = formatValidationResult.IsValid;

                validationData
                    .FormatValidationErrors
                    .AddRange(formatValidationResult
                        .Errors
                        .Select(e => new ValidationError { Message = e.Message }));

                SmartContractValidationResult determinismValidationResult = determinismValidator.Validate(moduleDefinition);

                validationData.DeterminismValid = determinismValidationResult.IsValid;

                validationData
                    .DeterminismValidationErrors
                    .AddRange(determinismValidationResult.Errors);

                SmartContractValidationResult warningResult = warningValidator.Validate(moduleDefinition.ModuleDefinition);

                validationData
                    .Warnings
                    .AddRange(warningResult
                        .Errors
                        .Select(e => new Warning { Message = e.Message }));
            }

            List<IReportSection> reportStructure = new List<IReportSection>();
            reportStructure.Add(new HeaderSection());
            reportStructure.Add(new CompilationSection());

            reportStructure.Add(new FormatSection());
            reportStructure.Add(new DeterminismSection());

            reportStructure.Add(new WarningsSection());

            if (this.ShowBytes)
                reportStructure.Add(new ByteCodeSection());

            reportStructure.Add(new FooterSection());

            var renderer = new StreamTextRenderer(Console.Out);

            foreach (ValidationReportData data in reportData)
            {
                renderer.Render(reportStructure, data);

                if (this.WriteBytes && data.DeterminismValid && data.FormatValid)
                {
                    var fn = Path.ChangeExtension(data.FileName, ".cil");

                    using (var sw = new StreamWriter(fn))
                    {
                        sw.Write(data.CompilationBytes.ToHexString());
                    }
                }
            }

            if (Debugger.IsAttached)
                Console.ReadLine();

            return 1;
        }
    }
}