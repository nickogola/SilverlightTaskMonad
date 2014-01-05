using System.Threading;
using System.Threading.Tasks;
using Microsoft.Silverlight.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestApp
{
    [TestClass]
    [Tag("Task")]
    [Tag("Base")]
    public class TaskBaseTests
    {
        [TestMethod]
        public void TaskFactory_ProducesCancelledTaskIfFunctionThrowsTaskCancelledExceptionFromCancellationToken()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancellationToken = cts.Token;
            var res = Task.Factory.StartNew(cancellationToken.ThrowIfCancellationRequested, cancellationToken);

            while (!res.IsCanceled && !res.IsCompleted && !res.IsFaulted)
            {
                Thread.SpinWait(100);
            }

            Assert.AreEqual(TaskStatus.Canceled, res.Status);
        }
    }
}
