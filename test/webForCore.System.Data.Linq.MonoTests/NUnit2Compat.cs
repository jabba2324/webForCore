// webForCore: NUnit 2 compatibility for Mono's System.Data.Linq tests (NUnit 3 removed [ExpectedException]), as for
// Mono's System.Web tests (test/webForCore.System.Web.MonoTests/NUnit2Compat.cs). Follows the IWrapTestMethod pattern
// from NUnit 3's documentation: the test passes only if it throws exactly the expected exception type (NUnit 2
// semantics; derived types do not count).
using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ExpectedExceptionAttribute : NUnitAttribute, IWrapTestMethod
    {
        private readonly Type _expectedExceptionType;

        public ExpectedExceptionAttribute(Type type)
        {
            _expectedExceptionType = type;
        }

        public TestCommand Wrap(TestCommand command)
        {
            return new ExpectedExceptionCommand(command, _expectedExceptionType);
        }

        private sealed class ExpectedExceptionCommand : DelegatingTestCommand
        {
            private readonly Type _expectedType;

            public ExpectedExceptionCommand(TestCommand innerCommand, Type expectedType) : base(innerCommand)
            {
                _expectedType = expectedType;
            }

            public override TestResult Execute(TestExecutionContext context)
            {
                Type caughtType = null;
                string caughtMessage = null;
                try
                {
                    innerCommand.Execute(context);
                }
                catch (Exception ex)
                {
                    if (ex is NUnitException && ex.InnerException != null)
                        ex = ex.InnerException;
                    caughtType = ex.GetType();
                    caughtMessage = ex.Message;
                }

                if (caughtType == _expectedType)
                    context.CurrentResult.SetResult(ResultState.Success);
                else if (caughtType != null)
                    context.CurrentResult.SetResult(ResultState.Failure,
                        $"Expected {_expectedType.FullName} but got {caughtType.FullName}: {caughtMessage}");
                else if (context.CurrentResult.ResultState.Status == TestStatus.Failed)
                    { /* keep the assertion failure */ }
                else
                    context.CurrentResult.SetResult(ResultState.Failure,
                        $"Expected {_expectedType.FullName} but no exception was thrown");
                return context.CurrentResult;
            }
        }
    }
}
