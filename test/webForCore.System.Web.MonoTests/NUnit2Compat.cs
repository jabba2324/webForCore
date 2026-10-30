// webForCore: NUnit 2 compatibility for Mono's System.Web tests (NUnit 3 removed [ExpectedException]).
// Follows the IWrapTestMethod pattern from NUnit 3's documentation: the test passes only if it throws
// exactly the expected exception type (NUnit 2 semantics; derived types do not count).
using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

// Mono annotates some tests with its MonoTODO attribute (internal in System.Web); a no-op stand-in keeps them unchanged.
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class MonoTODOAttribute : Attribute
{
    public MonoTODOAttribute() { }
    public MonoTODOAttribute(string comment) { }
}

// Runs System.Web's module initializer before any test, so the test process has what every .NET Framework process
// using System.Web has (Windows code pages, default DbProviderFactories; see System.Web FrameworkDefaults). Some tests
// call Encoding.GetEncoding("windows-1251") before they touch any System.Web type.
[SetUpFixture]
public sealed class WebForCoreTestEnvironment
{
    [OneTimeSetUp]
    public void InitializeSystemWeb()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(System.Web.HttpContext).Module.ModuleHandle);
    }
}

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
