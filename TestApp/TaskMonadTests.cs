using Microsoft.Silverlight.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskMonad;

namespace TestApp
{
    [TestClass]
    [Tag("Task")]
    [Tag("TaskMonad")]
    public class TaskMonadTests
    {
        [TestMethod]
        public void Unit_ReturnsACompletedTask()
        {
            var taskResult = new object();

            var task = taskResult.Unit();

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.AreSame(taskResult, task.Result);
        }

        [TestMethod]
        public void Unit_AcceptsNull()
        {
            object result = null;

            var task = result.Unit();

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.IsNull(task.Result);
        }

        [TestMethod]
        public void AsTask_ReturnsACompletedTask()
        {
            var taskResult = new TestValue();

            var task = taskResult.AsTask();

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.AreSame(taskResult, task.Result);
        }

        [TestMethod]
        public void AsTask_AcceptsNull()
        {
            TestValue result = null;

            var task = result.AsTask();

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
            Assert.IsNull(task.Result);
        }

        [TestMethod]
        public void CreateCancelled_ProducesACancelledTask()
        {
            var task = TaskMonad.TaskMonad.CreateCancelled<TestValue>();

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Canceled, task.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void FromExceptions_ThrowsOnNullExceptions()
        {
            TaskMonad.TaskMonad.FromExceptions<TestValue>(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FromExceptions_ThrowsOnEmptyExceptions()
        {
            TaskMonad.TaskMonad.FromExceptions<TestValue>(Enumerable.Empty<Exception>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void FromExceptions_ThrowsOnNullExceptionItem()
        {
            TaskMonad.TaskMonad.FromExceptions<TestValue>(new[] { new Exception(), null, new Exception() });
        }

        [TestMethod]
        public void FromExceptions_ProducesAFaultedTask()
        {
            var task = TaskMonad.TaskMonad.FromExceptions<TestValue>(new[] { new Exception() });

            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Faulted, task.Status);
        }

        [TestMethod]
        public void FromExceptions_AllExceptionsInInnerExceptions()
        {
            var exceptions = new Exception[] { new ArgumentException(), new InvalidOperationException(), new ArgumentOutOfRangeException(), new ObjectDisposedException(""), new IOException(),
            new AggregateException(new ArgumentException(), new ArgumentOutOfRangeException()), new OperationCanceledException()};

            var task = TaskMonad.TaskMonad.FromExceptions<TestValue>(exceptions);

            Assert.IsNotNull(task.Exception);
            CollectionAssert.AreEqual(exceptions, task.Exception.InnerExceptions);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void FromException_ThrowsIfNull()
        {
            TaskMonad.TaskMonad.FromException<TestValue>(null);
        }

        [TestMethod]
        public void FromException_ProducesACancelledTaskOnOperationCancelledException()
        {
            var result = TaskMonad.TaskMonad.FromException<TestValue>(new OperationCanceledException());

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Canceled, result.Status);
        }

        [TestMethod]
        public void FromException_ProducesACancelledTaskOnTaskCancelledException()
        {
            var result = TaskMonad.TaskMonad.FromException<TestValue>(new TaskCanceledException());

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Canceled, result.Status);
        }

        [TestMethod]
        public void FromException_ProducesAFaultedTask()
        {
            var ex = new IOException();

            var result = TaskMonad.TaskMonad.FromException<TestValue>(ex);

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Faulted, result.Status);
            Assert.IsNotNull(result.Exception);
            Assert.AreEqual(1, result.Exception.InnerExceptions.Count);
            Assert.AreSame(ex, result.Exception.InnerException);
        }

        [TestMethod]
        public void FromException_UnwrapsAggregateException()
        {
            var ex = new AggregateException(new IOException(), new ArgumentNullException(), new ArgumentException(), new AggregateException());

            var result = TaskMonad.TaskMonad.FromException<TestValue>(ex);

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Faulted, result.Status);
            Assert.IsNotNull(result.Exception);
            CollectionAssert.AreEqual(ex.InnerExceptions, result.Exception.InnerExceptions);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CloneResult_ThrowsOnNullTask()
        {
            ((Task<TestValue>)null).CloneResult();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CloneResult_ThrowsOnWaitingForActivationTask()
        {
            var tcs = new TaskCompletionSource<TestValue>();
            var task = tcs.Task;
            Debug.Assert(task.Status == TaskStatus.WaitingForActivation, "task.Status == TaskStatus.WaitingForActivation");
            task.CloneResult();
        }

        // TODO: make the test app run in full trust to be able to use custom task schedulers
        //[TestMethod]
        //[ExpectedException(typeof(ArgumentException))]
        //public void CloneResult_ThrowsOnWaitingToRunTask()
        //{
        //    var task = Task.Factory.StartNew(_ => "", null, CancellationToken.None, TaskCreationOptions.None, new FrozenTaskScheduler());
        //    Debug.Assert(task.Status == TaskStatus.WaitingToRun, "task.Status == TaskStatus.WaitingToRun");
        //    task.CloneResult();
        //}

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CloneResult_ThrowsOnRunningTask()
        {
            var wh = new ManualResetEvent(false);
            var taskStartedWH = new ManualResetEvent(false);
            var task = Task.Factory.StartNew(() =>
            {
                taskStartedWH.Set();
                wh.WaitOne();
                wh.Dispose();
                return "";
            });
            taskStartedWH.WaitOne();
            taskStartedWH.Dispose();
            Debug.Assert(task.Status == TaskStatus.Running, "task.Status == TaskStatus.Running");
            try
            {
                task.CloneResult();
            }
            finally
            {
                wh.Set();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        [Ignore]
        // TODO: Find out how to simulate a task waiting for children.
        public void CloneResult_ThrowsOnWaitingForChildrenToCompleteTask()
        {
            var wh = new ManualResetEvent(false);
            var taskStartedWH = new ManualResetEvent(false);

            var childWH = new ManualResetEvent(false);
            var childTaskStartedWH = new ManualResetEvent(false);

            var task = Task.Factory.StartNew(() =>
            {
                taskStartedWH.Set();
                wh.WaitOne();
                wh.Dispose();
                return "";
            });
            var childTask = task.ContinueWith(x =>
                {
                    childTaskStartedWH.Set();
                    childWH.WaitOne();
                    childWH.Dispose();
                    return x;
                }, TaskContinuationOptions.AttachedToParent);
            taskStartedWH.WaitOne();
            taskStartedWH.Dispose();
            wh.Set();
            childTaskStartedWH.WaitOne();
            childTaskStartedWH.Dispose();
            Debug.Assert(task.Status == TaskStatus.WaitingForChildrenToComplete, "task.Status == TaskStatus.WaitingForChildrenToComplete");
            try
            {
                task.CloneResult();
            }
            finally
            {
                childWH.Set();
            }
        }

        [TestMethod]
        public void CloneResult_ProducesACancelledTaskFromCancelledTask()
        {
            var source = TaskMonad.TaskMonad.CreateCancelled<TestValue>();

            var result = source.CloneResult();

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Canceled, result.Status);
        }

        [TestMethod]
        public void CloneResult_ProducesAFaultedTaskFromAFaultedTask()
        {
            var ex = new IOException();

            var task = TaskMonad.TaskMonad.FromException<TestValue>(ex);

            var result = task.CloneResult();

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Faulted, result.Status);
            Assert.IsNotNull(result.Exception);
            Assert.AreEqual(1, result.Exception.InnerExceptions.Count);
            Assert.AreSame(ex, result.Exception.InnerException);
        }

        [TestMethod]
        public void CloneResult_ProducesACompletedTaskFromACompletedTask()
        {
            var task = new TestValue().AsTask();

            var result = task.CloneResult();

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.RanToCompletion, result.Status);
            Assert.AreSame(task.Result, result.Result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Bind_ThrowsIfSourceTaskNull()
        {
            ((Task<TestValue>)null).Bind(x => x.Unit());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Bind_ThrowsIfFuncNull()
        {
            new TestValue().AsTask().Bind<TestValue, TestValue>(null);
        }

        [TestMethod]
        public void Bind_ProducesCancelledTaskOfCancelledSource()
        {
            var tcs = new TaskCompletionSource<TestValue>();
            tcs.SetCanceled();

            var source = tcs.Task;

            var result = source.Bind(x =>
            {
                return x.Unit();
            });            

            Assert.IsNotNull(result);
            Assert.AreEqual(TaskStatus.Canceled, result.Status);
        }

        class TestValue { }
    }
}
