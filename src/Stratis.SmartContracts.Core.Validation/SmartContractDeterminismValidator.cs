using System.Linq;
using Mono.Cecil;
using Stratis.ModuleValidation.Net;

namespace Stratis.SmartContracts.Core.Validation
{
    /// <summary>
    /// Checks for non-deterministic properties inside smart contracts by validating them at the bytecode level.
    /// </summary>
    public class SmartContractDeterminismValidator : ISmartContractValidator
    {
        /// <inheritdoc/>
        public SmartContractValidationResult Validate(SmartContractDecompilation decompilation)
        {
            TypeDefinition type =
                decompilation.ModuleDefinition.Types.FirstOrDefault(x => x.FullName != "<Module>");

            if (type == null)
                return new SmartContractValidationResult(Enumerable.Empty<ValidationResult>());
           
            var policy = DeterminismPolicyFactory.CreatePolicy();

            var validator = new TypePolicyValidator(policy);

            var validationResult = validator.Validate(type).ToList();

            return new SmartContractValidationResult(validationResult);
        }
    }
}