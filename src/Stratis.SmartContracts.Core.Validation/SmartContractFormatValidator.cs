using System.Linq;

namespace Stratis.SmartContracts.Core.Validation
{
    /// <summary>
    /// Validates the format of a Smart Contract <see cref="SmartContractDecompilation"/>
    /// </summary>
    public class SmartContractFormatValidator : ISmartContractValidator
    {
<<<<<<< HEAD
=======
        private static readonly List<IModuleDefinitionValidator> ModuleDefinitionValidators = new List<IModuleDefinitionValidator>
        {
            new SmartContractTypeDefinitionValidator()
        };

        public SmartContractFormatValidator(IEnumerable<Assembly> allowedAssemblies)
        {
            // TODO - Factor out allowed assemblies
            ModuleDefinitionValidators.Add(new AssemblyReferenceValidator(allowedAssemblies));
        }

>>>>>>> master
        public SmartContractValidationResult Validate(SmartContractDecompilation decompilation)
        {
            ValidationPolicy policy = FormatPolicy.Default;

            var validator = new ModulePolicyValidator(policy);

            var results = validator.Validate(decompilation.ModuleDefinition).ToList();

            return new SmartContractValidationResult(results);
        }
    }
}