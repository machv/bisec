using System;
using System.Threading.Tasks;

namespace BiSec.Library
{
    public abstract class PackageQueueItem
    {
        private Package _package;
        public Package Package => _package;
        public virtual Task Completion => Task.CompletedTask;

        public virtual void SetResult(Package result) { }
        public virtual void SetException(Exception exception) { }

        public PackageQueueItem(Package package)
        {
            _package = package;
        }
    }

    public class PackageQueueItem<T> : PackageQueueItem where T : Package
    {
        private TaskCompletionSource<T> _tcs;
        public TaskCompletionSource<T> TaskCompletionSource => _tcs;
        public override Task Completion => _tcs.Task;

        public override void SetResult(Package result)
        {
            SetResult((T)result);
        }
        public void SetResult(T result)
        {
            if (_tcs != null)
                _tcs.TrySetResult(result);
        }

        public override void SetException(Exception exception)
        {
            if (_tcs != null)
                _tcs.TrySetException(exception);
        }

        public PackageQueueItem(Package package, TaskCompletionSource<T> tcs) : base(package)
        {
            _tcs = tcs;
        }
    }
}
