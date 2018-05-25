﻿using System.Collections.Generic;
using Mono.Cecil;

namespace Stratis.SmartContracts.Core.ContractValidation
{
    public interface IMethodDefinitionValidator
    {
        IEnumerable<SmartContractValidationError> Validate(MethodDefinition method);
    }
}