﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Stratis.ModuleValidation.Net
{
    public interface IInstructionValidator
    {
        IEnumerable<ValidationResult> Validate(Instruction instruction, MethodDefinition method);
    }
}