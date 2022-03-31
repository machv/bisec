using System.Threading.Tasks;

namespace BiSec.Library
{
    public abstract class PackageQueueItem
    {
        private Package _package;
        public Package Package => _package;

        public virtual void SetResult(Package result) { }

        public PackageQueueItem(Package package)
        {
            _package = package;
        }
    }

    public class PackageQueueItem<T> : PackageQueueItem where T : Package
    {
        private TaskCompletionSource<T> _tcs;
        public TaskCompletionSource<T> TaskCompletionSource => _tcs;

        public override void SetResult(Package result)
        {
            SetResult((T)result);
        }
        public void SetResult(T result)
        {
            if (_tcs != null)
                _tcs.SetResult(result); // Try?
        }

        public PackageQueueItem(Package package, TaskCompletionSource<T> tcs) : base(package)
        {
            _tcs = tcs;
        }
    }
}
