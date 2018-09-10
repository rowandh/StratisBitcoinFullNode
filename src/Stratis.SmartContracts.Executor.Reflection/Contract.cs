using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NBitcoin;
using Stratis.SmartContracts.Executor.Reflection.Exceptions;

namespace Stratis.SmartContracts.Executor.Reflection
{
    /// <summary>
    /// A class representing a contract instance. Manages the lifecycle of a contract object and allows for constructor and method invocations.
    /// </summary>
    public class Contract : IContract
    {
        /// <summary>
        /// The default binding flags for matching the receive method. Matches public instance methods declared on the contract type only.
        /// </summary>
        private const BindingFlags DefaultReceiveLookup = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public;

        private readonly SmartContract instance;

        /// <summary>
        /// Indicates whether the ISmartContractState fields have been set on the instance object yet.
        /// </summary>
        private bool initialized;

        /// <inheritdoc />
        public uint160 Address { get; }

        /// <inheritdoc />
        public Type Type { get; }

        /// <inheritdoc />
        public ISmartContractState State { get; }

        private MethodInfo receive;

        /// <summary>
        /// Returns the receive handler method defined on the inherited contract type. If no receive handler was defined, returns null.
        /// </summary>
        public MethodInfo ReceiveHandler {
            get
            {
                if (this.receive == null)
                {
                    this.receive = this.Type.GetMethod(MethodCall.ReceiveHandlerName, DefaultReceiveLookup);
                }

                return this.receive;
            }
        }

        private Contract(SmartContract instance, Type type, ISmartContractState state, uint160 address)
        {
            this.instance = instance;
            this.State = state;
            this.Type = type;
            this.Address = address;
        }

        /// <summary>
        /// Creates an <see cref="IContract"/> that represents a smart contract in an uninitialized state.
        /// </summary>
        public static IContract CreateUninitialized(Type type, ISmartContractState state, uint160 address)
        {
            var contract = (SmartContract)FormatterServices.GetSafeUninitializedObject(type);

            return new Contract(contract, type, state, address);
        }

        /// <summary>
        /// Checks whether a the constructor with the given signature exists on the supplied contract Type.
        /// </summary>
        public static bool ConstructorExists(Type type, IReadOnlyList<object> parameters)
        {
            Type[] types = {typeof(ISmartContractState)};    
            
            if (parameters != null)
                types = types.Concat(parameters.Select(p => p.GetType())).ToArray();

            return type.GetConstructor(types) != null;
        }

        /// <inheritdoc />
        public IContractInvocationResult InvokeConstructor(IReadOnlyList<object> parameters)
        {
            // If it's a constructor we need to append the ISmartContractState to the start of the parameters array
            object[] invokeParams = { this.State };
            
            if(parameters != null)
                invokeParams = invokeParams.Concat(parameters).ToArray();

            Type[] types = invokeParams.Select(p => p.GetType()).ToArray();

            ConstructorInfo methodToInvoke = this.Type.GetConstructor(types);

            if (methodToInvoke == null)
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodDoesNotExist);

            IContractInvocationResult result = this.InvokeInternal(methodToInvoke, invokeParams);

            this.initialized = result.IsSuccess;

            return result;
        }

        /// <inheritdoc />
        public IContractInvocationResult Invoke(MethodCall call)
        {
            if (call.IsReceiveHandlerCall)
            {
                return this.InvokeReceiveHandler();
            }

            object[] invokeParams = call.Parameters?.ToArray() ?? new object[0];

            Type[] types = invokeParams.Select(p => p.GetType()).ToArray();

            MethodInfo methodToInvoke = this.Type.GetMethod(call.Name, types);

            if (methodToInvoke == null)
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodDoesNotExist);

            // This should not happen without setting the appropriate binding flags
            if (methodToInvoke.IsConstructor)
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodIsConstructor);

            // This should not happen without setting the appropriate binding flags
            if (methodToInvoke.IsPrivate)
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodIsPrivate);

            EnsureInitialized();

            return this.InvokeInternal(methodToInvoke, invokeParams);
        }

        private IContractInvocationResult InvokeReceiveHandler()
        {
            // Handles the scenario where no receive was defined, but it is attempted to be invoked anyway.
            // This could occur if a method invocation is directly made to the receive via a transaction.
            if (this.ReceiveHandler == null)
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodDoesNotExist);

            EnsureInitialized();

            return this.InvokeInternal(this.ReceiveHandler, null);
        }

        /// <summary>
        /// Ensures the contract is initialized by setting its state fields.
        /// </summary>
        public void EnsureInitialized()
        {
            if (!this.initialized)
                SetStateFields(this.instance, this.State);

            this.initialized = true;
        }

        /// <summary>
        /// Shared internal logic for invoking a method.
        /// </summary>
        private IContractInvocationResult InvokeInternal(MethodBase method, object[] parameters)
        {
            try
            {
                object result = method.Invoke(this.instance, parameters);

                return ContractInvocationResult.Success(result);
            }
            catch (ArgumentException argumentException)
            {
                // Parameters do not match
                // This should not happen
                return ContractInvocationResult.Failure(ContractInvocationErrorType.ParameterTypesDontMatch, argumentException);
            }
            catch (TargetInvocationException targetException) when (!(targetException.InnerException is OutOfGasException))
            {
                // Method threw an exception that was not an OutOfGasException
                return ContractInvocationResult.Failure(ContractInvocationErrorType.MethodThrewException, targetException);
            }
            catch (TargetInvocationException targetException) when (targetException.InnerException is OutOfGasException)
            {
                // Method threw an exception that was an OutOfGasException
                return ContractInvocationResult.Failure(ContractInvocationErrorType.OutOfGas);
            }
            catch (TargetParameterCountException parameterException)
            {
                // Parameter count incorrect
                // This should not happen
                return ContractInvocationResult.Failure(ContractInvocationErrorType.ParameterCountIncorrect, parameterException);
            }            
        }

        public static (TimeSpan, TimeSpan) SetStateField(SmartContract smartContract, ISmartContractState contractState)
        {
            var sw = new Stopwatch();

            sw.Start();
            var field = typeof(SmartContract).GetField("smartContractState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            sw.Stop();

            var getField = sw.Elapsed;

            sw.Restart();

            field.SetValue(smartContract, contractState);

            sw.Stop();

            return (getField, sw.Elapsed);
        }


        /// <summary>
        /// Uses reflection to set the state fields on the contract object.
        /// </summary>
        public static (TimeSpan, TimeSpan) SetStateFields2(SmartContract smartContract, ISmartContractState contractState)
        {
            var sw = new Stopwatch();
            sw.Start();

            FieldInfo[] fields = typeof(SmartContract).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            sw.Stop();

            var getField = sw.Elapsed;

            sw.Reset();

            foreach (FieldInfo field in fields)
            {
                switch (field.Name)
                {
                    case "gasMeter":
                        sw.Start();
                        field.SetValue(smartContract, contractState.GasMeter);
                        sw.Stop();
                        break;
                    case "Block":
                        sw.Start();

                        field.SetValue(smartContract, contractState.Block);
                        sw.Stop();
                        break;
                    case "getBalance":
                        sw.Start();

                        field.SetValue(smartContract, contractState.GetBalance);
                        sw.Stop();
                        break;
                    case "internalTransactionExecutor":
                        sw.Start();

                        field.SetValue(smartContract, contractState.InternalTransactionExecutor);
                        sw.Stop();
                        break;
                    case "internalHashHelper":
                        sw.Start();

                        field.SetValue(smartContract, contractState.InternalHashHelper);
                        sw.Stop();
                        break;
                    case "Message":
                        sw.Start();

                        field.SetValue(smartContract, contractState.Message);
                        sw.Stop();
                        break;
                    case "PersistentState":
                        sw.Start();

                        field.SetValue(smartContract, contractState.PersistentState);
                        sw.Stop();
                        break;
                    case "smartContractState":
                        sw.Start();

                        field.SetValue(smartContract, contractState);
                        sw.Stop();
                        break;
                    case "Serializer":
                        sw.Start();

                        field.SetValue(smartContract, contractState.Serializer);
                        sw.Stop();
                        break;
                    default:
                        break;
                }
            }
            return (getField, sw.Elapsed);
        }

        /// <summary>
        /// Uses reflection to set the state fields on the contract object.
        /// </summary>
        public static void SetStateFields(SmartContract smartContract, ISmartContractState contractState)
        {
            FieldInfo[] fields = typeof(SmartContract).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (FieldInfo field in fields)
            {
                switch (field.Name)
                {
                    case "gasMeter":
                        field.SetValue(smartContract, contractState.GasMeter);
                        break;
                    case "Block":
                        field.SetValue(smartContract, contractState.Block);
                        break;
                    case "getBalance":
                        field.SetValue(smartContract, contractState.GetBalance);
                        break;
                    case "internalTransactionExecutor":
                        field.SetValue(smartContract, contractState.InternalTransactionExecutor);
                        break;
                    case "internalHashHelper":
                        field.SetValue(smartContract, contractState.InternalHashHelper);
                        break;
                    case "Message":
                        field.SetValue(smartContract, contractState.Message);
                        break;
                    case "PersistentState":
                        field.SetValue(smartContract, contractState.PersistentState);
                        break;
                    case "smartContractState":
                        field.SetValue(smartContract, contractState);
                        break;
                    case "Serializer":
                        field.SetValue(smartContract, contractState.Serializer);
                        break;
                    default:
                        break;
                }
            }
        }
    }
}