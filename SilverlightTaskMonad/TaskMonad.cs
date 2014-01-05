using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace TaskMonad
{
    public static class TaskMonad
    {
        private static readonly Task _CompletedCached = new VoidResult().AsTask();

        private static readonly Task _CancelledCached = CreateCancelled<VoidResult>();

        public static Task<T> Unit<T>(this T value)
        {
            return value.AsTask();
        }

        public static Task<TResult> Bind<TSource, TResult>(this Task<TSource> source, Func<TSource, Task<TResult>> func)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            return source.ContinueWith(t =>
            {
                switch (t.Status)
                {
                    case TaskStatus.Created:
                    case TaskStatus.Running:
                    case TaskStatus.WaitingForActivation:
                    case TaskStatus.WaitingForChildrenToComplete:
                    case TaskStatus.WaitingToRun:
                        throw new InvalidOperationException(string.Format("Unexpected task status '{0}'", t.Status));
                    case TaskStatus.Canceled:
                        return CreateCancelled<TResult>();
                    case TaskStatus.Faulted:
                        return FromException<TResult>(t.Exception);
                    case TaskStatus.RanToCompletion:
                        var resultTask = func(t.Result);
                        if (resultTask == null)
                        {
                            throw new InvalidOperationException("The bound func has returned a null task");
                        }
                        return resultTask;
                    default:
                        throw new ArgumentOutOfRangeException("t.Status", "Unexpected task status");
                }
            },
                TaskContinuationOptions.ExecuteSynchronously)
                .Unwrap();
        }

        public static Task<T> AsTask<T>(this T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        public static Task<T> FromException<T>(Exception ex)
        {
            if (ex == null)
            {
                throw new ArgumentNullException("ex");
            }

            // todo: revert this when CancellationToken support is added
            //if (ex is OperationCanceledException)
            //{
            //    return CreateCancelled<T>();
            //}

            var aggrEx = ex as AggregateException;
            if (aggrEx != null)
            {
                return FromExceptions<T>(aggrEx.InnerExceptions);
            }

            var tcs = new TaskCompletionSource<T>();
            tcs.SetException(ex);
            return tcs.Task;
        }

        public static Task<T> FromExceptions<T>(IEnumerable<Exception> exceptions)
        {
            if (exceptions == null)
            {
                throw new ArgumentNullException("exceptions");
            }

            var tcs = new TaskCompletionSource<T>();
            tcs.SetException(exceptions);
            return tcs.Task;
        }

        public static Task<T> CloneResult<T>(this Task<T> t)
        {
            if (t == null)
            {
                throw new ArgumentNullException("t");
            }

            var tcs = new TaskCompletionSource<T>();

            if (t.IsCanceled)
            {
                tcs.SetCanceled();
                return tcs.Task;
            }
            if (t.IsFaulted)
            {
                tcs.SetException(t.Exception.InnerExceptions);
                return tcs.Task;
            }
            if (t.IsCompleted)
            {
                tcs.SetResult(t.Result);
                return tcs.Task;
            }

            throw new ArgumentException("The task has not finished yet.", "t");
        }

        public static Task<T> CreateCancelled<T>()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        public static Task<TResult> Then<TSource, TResult>(this Task<TSource> task, Func<TSource, TResult> func)
        {
            if (task == null)
            {
                throw new ArgumentNullException("task");
            }
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            return task.Bind(x => func(x).Unit());
        }

        public static Task<TResult> Then<TSource, TResult>(this Task<TSource> task, Func<TSource, Task<TResult>> func)
        {
            return task.Bind(func);
        }

        public static Task<T> Using<T>(Func<Task<IDisposable>> resourceFactory, Func<IDisposable, Task<T>> resourceUsage)
        {
            if (resourceFactory == null)
            {
                throw new ArgumentNullException("resourceFactory");
            }
            if (resourceUsage == null)
            {
                throw new ArgumentNullException("resourceUsage");
            }

            return resourceFactory()
                .Then(resource =>
                    Try(() => resourceUsage(resource),
                        null,
                        () =>
                        {
                            if (resource != null)
                                resource.Dispose();
                            return _CompletedCached;
                        })
                );
        }

        public static Task<T> Try<T>(Func<Task<T>> tryBlock, IEnumerable<ICatchBlock<T>> catchBlocks,
            Func<Task> finallyBlock)
        {
            if (tryBlock == null)
                throw new ArgumentNullException("tryBlock");
            var cachedCatchBlocks = (catchBlocks ?? Enumerable.Empty<ICatchBlock<T>>()).ToList();
            if (finallyBlock == null || !cachedCatchBlocks.Any())
            {
                throw new ArgumentException("Either a catch block, or the finally block is required.");
            }

            Task<T> tryBlockAsync;
            try
            {
                tryBlockAsync = tryBlock();
            }
            // todo: revert this when CancellationToken support is added
            //catch (OperationCanceledException)
            //{
            //    return CreateCancelled<T>();
            //}
            catch (Exception ex)
            {
                return HandleException(cachedCatchBlocks, finallyBlock, ex);
            }

            if (tryBlockAsync == null)
            {
                return FromException<T>(new InvalidOperationException("tryBlock has returned null"));
            }

            return tryBlockAsync.ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    return CreateCancelled<T>();
                }

                if (!t.IsFaulted)
                {
                    return ExecuteFinallyBlock(finallyBlock, t);
                }

                var exceptionToHandle = t.Exception.GetBaseException();
                return HandleException(cachedCatchBlocks, finallyBlock, exceptionToHandle);
            },
                TaskContinuationOptions.ExecuteSynchronously)
                .Unwrap();
        }

        private static Task<T> HandleException<T>(IEnumerable<ICatchBlock<T>> catchBlocks, Func<Task> finallyBlock,
            Exception ex)
        {
            Debug.Assert(finallyBlock != null, "finallyBlock != null");
            Debug.Assert(catchBlocks != null, "catchBlocks != null");
            Debug.Assert(ex != null, "ex != null");

            var matchedCatchBlocks = catchBlocks
                .Where(x => !x.IsSpecificExceptionHandler || x.HandledExceptionType.IsInstanceOfType(ex));
            var catchBlockToExecute = matchedCatchBlocks.FirstOrDefault();
            if (catchBlockToExecute == null)
                return ExecuteFinallyBlock(finallyBlock, FromException<T>(ex));

            Task<T> handleResultAsync;
            try
            {
                handleResultAsync = catchBlockToExecute.Handle(FromException<T>(ex), ex);
            }
            // todo: revert this when CancellationToken support is added
            //catch (OperationCanceledException)
            //{
            //    return CreateCancelled<T>();
            //}
            catch (Exception handleEx)
            {
                var finallySuccessResult = FromException<T>(handleEx);
                return ExecuteFinallyBlock(finallyBlock, finallySuccessResult);
            }

            if (handleResultAsync == null)
            {
                return FromException<T>(new InvalidOperationException("A catchBlock has returned null"));
            }

            return handleResultAsync
                .ContinueWith(handleTask =>
                {
                    if (handleTask.IsCanceled)
                    {
                        return CreateCancelled<T>();
                    }

                    return ExecuteFinallyBlock(finallyBlock, handleTask);
                },
                    TaskContinuationOptions.ExecuteSynchronously)
                .Unwrap();
        }

        private static Task<T> ExecuteFinallyBlock<T>(Func<Task> finallyBlock, Task<T> finallySuccessResult)
        {
            Debug.Assert(finallyBlock != null, "finallyBlock != null");
            Debug.Assert(finallySuccessResult != null, "finallySuccessResult != null");

            Task finallyBlockAsync;
            try
            {
                finallyBlockAsync = finallyBlock();
            }
            // todo: revert this when CancellationToken support is added
            //catch (OperationCanceledException)
            //{
            //    return CreateCancelled<T>();
            //}
            catch (Exception finallyEx)
            {
                return FromException<T>(finallyEx);
            }

            if (finallyBlockAsync == null)
            {
                return FromException<T>(new InvalidOperationException("finallyBlock has returned null"));
            }

            return finallyBlockAsync
                .ContinueWith(ft =>
                {
                    if (ft.IsCanceled)
                    {
                        return CreateCancelled<T>();
                    }

                    if (ft.IsFaulted)
                    {
                        return FromException<T>(ft.Exception);
                    }

                    return finallySuccessResult;
                },
                    TaskContinuationOptions.ExecuteSynchronously)
                .Unwrap();
        }

        public class CatchInfo<TResult, TException>
            where TException : Exception
        {
            private readonly TException _exception;
            private readonly Task<TResult> _failedTask;

            public CatchInfo(Task<TResult> failedTask, TException exception)
            {
                if (failedTask == null)
                {
                    throw new ArgumentNullException("failedTask");
                }
                if (exception == null)
                {
                    throw new ArgumentNullException("exception");
                }

                _failedTask = failedTask;
                _exception = exception;
            }

            public TException Exception
            {
                get { return _exception; }
            }

            public CatchResult Handled(TResult newValue)
            {
                return new CatchResult(newValue.Unit());
            }

            public CatchResult Throw(Exception ex)
            {
                return new CatchResult(FromException<TResult>(ex));
            }

            public CatchResult Rethrow()
            {
                return new CatchResult(_failedTask);
            }

            public CatchResult Then(Task<TResult> continuation)
            {
                return new CatchResult(continuation);
            }

            public class CatchResult
            {
                public CatchResult(Task<TResult> task)
                {
                    if (task == null)
                    {
                        throw new ArgumentNullException("task");
                    }

                    Task = task;
                }

                public Task<TResult> Task { get; private set; }
            }
        }

        public class CatchInfo<TResult>
        {
            private readonly Exception _exception;
            private readonly Task<TResult> _failedTask;

            public CatchInfo(Task<TResult> failedTask, Exception exception)
            {
                if (failedTask == null)
                {
                    throw new ArgumentNullException("failedTask");
                }
                if (exception == null)
                {
                    throw new ArgumentNullException("exception");
                }

                _failedTask = failedTask;
                _exception = exception;
            }

            public Exception Exception
            {
                get { return _exception; }
            }

            public CatchResult Handled(TResult newValue)
            {
                return new CatchResult(newValue.Unit());
            }

            public CatchResult Throw(Exception ex)
            {
                return new CatchResult(FromException<TResult>(ex));
            }

            public CatchResult Rethrow()
            {
                return new CatchResult(_failedTask);
            }

            public CatchResult Then(Task<TResult> continuation)
            {
                return new CatchResult(continuation);
            }

            public class CatchResult
            {
                public CatchResult(Task<TResult> task)
                {
                    if (task == null)
                    {
                        throw new ArgumentNullException("task");
                    }

                    Task = task;
                }

                public Task<TResult> Task { get; private set; }
            }
        }

        public interface ICatchBlock<T>
        {
            Type HandledExceptionType { get; }

            bool IsSpecificExceptionHandler { get; }

            Task<T> Handle(Task<T> faultedTask, Exception exception);
        }

        private struct VoidResult
        {
        }
    }
}