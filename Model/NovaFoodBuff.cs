using System;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    public class NovaFoodBuff : Action
    {
        public const string ACTION_NAME_NOVA_FOOD_BUFF = "NovaFoodBuff";
        public static event System.Action<string> NovaFoodLogged;
        public static event System.Action<bool> NovaFoodToggled;

        public Key manualBookItemKey { get; set; } = Key.D8;
        public int checkIntervalMs { get; set; } = 800;
        public int stepDelayMs { get; set; } = 350;
        public bool isEnabled { get; set; } = false;

        private _4RThread thread;
        private DateTime nextAttemptAtUtc = DateTime.MinValue;

        public void Start()
        {
            Stop();
            Client client = ClientSingleton.GetClient();
            if (client != null)
            {
                nextAttemptAtUtc = DateTime.MinValue;
                this.thread = new _4RThread(_ => Execution(client));
                _4RThread.Start(this.thread);
            }
        }

        public void Stop()
        {
            _4RThread.Stop(this.thread);
        }

        private int Execution(Client client)
        {
            if (!isEnabled)
            {
                Thread.Sleep(Math.Max(80, checkIntervalMs));
                return 0;
            }

            if (DateTime.UtcNow < nextAttemptAtUtc)
            {
                Thread.Sleep(Math.Max(80, checkIntervalMs));
                return 0;
            }

            if (HasNovaFoodBuff(client))
            {
                Thread.Sleep(Math.Max(80, checkIntervalMs));
                return 0;
            }

            if (!InputCoordinator.CanStartNovaSequence())
            {
                Thread.Sleep(Math.Max(80, checkIntervalMs));
                return 0;
            }

            Log("Buff 273 is inactive, trying Nova Food sequence.");
            ExecuteNovaFoodSequence();
            Thread.Sleep(Math.Max(0, stepDelayMs + 300));

            if (!HasNovaFoodBuff(client))
            {
                isEnabled = false;
                ProfileSingleton.SetConfiguration(this);
                NovaFoodToggled?.Invoke(false);
                Log("Nova Food buff still missing after sequence. Item likely out of stock. Feature turned OFF.");
            }
            else
            {
                Log("Nova Food buff detected after sequence.");
            }

            nextAttemptAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, checkIntervalMs));
            Thread.Sleep(Math.Max(80, checkIntervalMs));
            return 0;
        }

        private bool HasNovaFoodBuff(Client client)
        {
            for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
            {
                if (client.CurrentBuffStatusCode(i) == 273)
                {
                    return true;
                }
            }

            return false;
        }

        private void ExecuteNovaFoodSequence()
        {
            if (!FormUtils.IsValidKey(manualBookItemKey)) return;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) return;
            if (!InputCoordinator.BeginNovaSequence()) return;

            try
            {
                int delay = Math.Max(0, stepDelayMs);
                SendKey(manualBookItemKey);
                Thread.Sleep(delay);
                SendKey(Key.Enter);
                Thread.Sleep(delay);
                SendKey(Key.Enter);
                Thread.Sleep(delay);
                SendKey(Key.Down);
                Thread.Sleep(delay);
                SendKey(Key.Enter);
                Thread.Sleep(delay);
                SendKey(Key.Enter);
                Thread.Sleep(delay);
                SendKey(Key.Enter);
            }
            finally
            {
                InputCoordinator.EndNovaSequence();
            }
        }

        private void SendKey(Key key)
        {
            Keys k = (Keys)Enum.Parse(typeof(Keys), key.ToString());
            IntPtr target = ClientSingleton.GetClient().process.MainWindowHandle;
            Interop.PostMessage(target, Constants.WM_KEYDOWN_MSG_ID, k, 0);
            Interop.PostMessage(target, Constants.WM_KEYUP_MSG_ID, k, 0);
        }

        private void Log(string message)
        {
            NovaFoodLogged?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public string GetConfiguration()
        {
            return JsonConvert.SerializeObject(this);
        }

        public string GetActionName()
        {
            return ACTION_NAME_NOVA_FOOD_BUFF;
        }
    }
}
