﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Stratis.ModuleValidation.Net;
using Stratis.ModuleValidation.Net.Format;
using Stratis.SmartContracts.Core.Validation.Validators.Module;
using Stratis.SmartContracts.Core.Validation.Validators.Type;

namespace Stratis.SmartContracts.Core.Validation
{
    public class FormatPolicyFactory
    {
        // System.Runtime forwards to mscorlib, so we can only get its Assembly by name
        // ref. https://github.com/dotnet/corefx/issues/11601
        private static readonly Assembly Runtime = Assembly.Load("System.Runtime");
        private static readonly Assembly Core = typeof(object).Assembly;

        /// <summary>
        /// The set of Assemblies that a <see cref="SmartContract"/> is required to reference
        /// </summary>
        public static HashSet<Assembly> AllowedAssemblies = new HashSet<Assembly> {
            Runtime,
            Core,
            typeof(SmartContract).Assembly,
            typeof(Enumerable).Assembly
        };

        public ValidationPolicy CreatePolicy()
        {
            return new ValidationPolicy()
                .ModuleDefValidator(new AssemblyReferenceValidator(AllowedAssemblies))
                .ModuleDefValidator(new SingleTypeValidator())
                .TypeDefValidator(new StaticConstructorValidator(), NestedTypePolicy.Ignore)
                .TypeDefValidator(new NamespaceValidator())
                .TypeDefValidator(new SingleConstructorValidator(), NestedTypePolicy.Ignore)
                .TypeDefValidator(new ConstructorParamValidator(), NestedTypePolicy.Ignore)
                .TypeDefValidator(new InheritsSmartContractValidator(), NestedTypePolicy.Ignore)
                .TypeDefValidator(new FieldDefinitionValidator(), NestedTypePolicy.Ignore)
                .NestedTypeDefValidator(new TypeHasMethodsValidator())
                .NestedTypeDefValidator(new TypeHasNestedTypesValidator())
                .NestedTypeDefValidator(new NestedTypesAreValueTypesValidator())
                .MethodDefValidator(new TryCatchValidator())
                .MethodDefValidator(new MethodParamValidator());
        }
    }

    /// <summary>
    /// Validates the format of a Smart Contract <see cref="SmartContractDecompilation"/>
    /// </summary>
    public class SmartContractFormatValidator : ISmartContractValidator
    {
        public SmartContractValidationResult Validate(SmartContractDecompilation decompilation)
        {
            var policy = new FormatPolicyFactory().CreatePolicy();

            var validator = new ModulePolicyValidator(policy);

            var results = validator.Validate(decompilation.ModuleDefinition).ToList();  

            return new SmartContractValidationResult(results);
        }  
    }
}
