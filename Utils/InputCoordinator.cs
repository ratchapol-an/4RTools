using System;
using System.Threading;
using System.Windows.Forms;

namespace _4RTools.Utils
{
    internal static class InputCoordinator
    {
        private static readonly object _inputLock = new object();
        private static bool _novaSequenceActive = false;
        private static long _lastHighPriorityActionTicksUtc = 0;

        public static bool TrySendHighPriorityKey(IntPtr target, Keys key)
        {
            if (!Monitor.TryEnter(_inputLock))
            {
                return false;
            }

            try
            {
                if (_novaSequenceActive)
                {
                    return false;
                }

                Interop.PostMessage(target, Constants.WM_KEYDOWN_MSG_ID, key, 0);
                Interop.PostMessage(target, Constants.WM_KEYUP_MSG_ID, key, 0);
                _lastHighPriorityActionTicksUtc = DateTime.UtcNow.Ticks;
                return true;
            }
            finally
            {
                Monitor.Exit(_inputLock);
            }
        }

        public static bool CanStartNovaSequence(int quietWindowMs = 250)
        {
            if (_novaSequenceActive)
            {
                return false;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            long elapsedTicks = nowTicks - Interlocked.Read(ref _lastHighPriorityActionTicksUtc);
            return elapsedTicks >= TimeSpan.FromMilliseconds(quietWindowMs).Ticks;
        }

        public static bool BeginNovaSequence()
        {
            if (!Monitor.TryEnter(_inputLock))
            {
                return false;
            }

            if (_novaSequenceActive)
            {
                Monitor.Exit(_inputLock);
                return false;
            }

            _novaSequenceActive = true;
            return true;
        }

        public static void EndNovaSequence()
        {
            if (!_novaSequenceActive)
            {
                return;
            }

            _novaSequenceActive = false;
            Monitor.Exit(_inputLock);
        }

        internal static void ResetForTests()
        {
            _novaSequenceActive = false;
            Interlocked.Exchange(ref _lastHighPriorityActionTicksUtc, 0);
        }

        internal static void MarkHighPriorityActionForTests()
        {
            Interlocked.Exchange(ref _lastHighPriorityActionTicksUtc, DateTime.UtcNow.Ticks);
        }
    }
}
