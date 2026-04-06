using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    public class AutoItemBuff : Action
    {
        public const string ACTION_NAME_AUTO_ITEM_BUFF = "AutoItemBuff";
        public static event System.Action<string> ItemBuffLogged;

        public Key agiItemKey { get; set; } = Key.None;
        public Key secondaryItemKey { get; set; } = Key.None;
        public int checkIntervalMs { get; set; } = 120;
        public bool isEnabled { get; set; } = false;

        private _4RThread thread;
        private bool? lastWantedAgiItem;
        private long lastSwitchTicksUtc;

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                lastWantedAgiItem = null;
                lastSwitchTicksUtc = 0;
                this.thread = new _4RThread(_ => Execution(roClient));
                _4RThread.Start(this.thread);
            }
        }

        public void Stop()
        {
            _4RThread.Stop(this.thread);
            lastWantedAgiItem = null;
            lastSwitchTicksUtc = 0;
        }

        private int Execution(Client client)
        {
            bool hasRequiredBuffs = HasRequiredBuffs(client);
            bool wantAgiItem = !hasRequiredBuffs;

            long nowTicks = DateTime.UtcNow.Ticks;
            long minSwitchIntervalTicks = TimeSpan.FromMilliseconds(250).Ticks;
            bool canSwitch = (nowTicks - lastSwitchTicksUtc) >= minSwitchIntervalTicks;

            if ((!lastWantedAgiItem.HasValue || lastWantedAgiItem.Value != wantAgiItem) && canSwitch)
            {
                if (wantAgiItem)
                {
                    SendKey(agiItemKey, "Required buff missing (Increase Agi or Blessing) -> switched to item 1.");
                }
                else
                {
                    SendKey(secondaryItemKey, "Increase Agi + Blessing active -> switched to item 2.");
                }

                lastWantedAgiItem = wantAgiItem;
                lastSwitchTicksUtc = nowTicks;
            }

            System.Threading.Thread.Sleep(Math.Max(40, checkIntervalMs));
            return 0;
        }

        private bool HasRequiredBuffs(Client client)
        {
            bool hasAgi = false;
            bool hasBless = false;
            for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
            {
                uint currentStatus = client.CurrentBuffStatusCode(i);
                EffectStatusIDs status = (EffectStatusIDs)currentStatus;

                if (status == EffectStatusIDs.INC_AGI)
                {
                    hasAgi = true;
                }

                if (status == EffectStatusIDs.BLESSING)
                {
                    hasBless = true;
                }

                if (hasAgi && hasBless)
                {
                    return true;
                }
            }

            return false;
        }

        private void SendKey(Key key, string reason)
        {
            if (!FormUtils.IsValidKey(key) || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                return;
            }

            Keys k = (Keys)Enum.Parse(typeof(Keys), key.ToString());
            IntPtr target = ClientSingleton.GetClient().process.MainWindowHandle;
            Interop.PostMessage(target, Constants.WM_KEYDOWN_MSG_ID, k, 0);
            Interop.PostMessage(target, Constants.WM_KEYUP_MSG_ID, k, 0);

            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {reason} Key={k}.";
            AppendLog(logLine);
            ItemBuffLogged?.Invoke(logLine);
        }

        private void AppendLog(string logLine)
        {
            string path = GetLogPath();
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.AppendAllText(path, logLine + Environment.NewLine);
        }

        public static string GetLogPath()
        {
            string profileName = ProfileSingleton.GetCurrent()?.Name ?? "default";
            return Path.Combine(AppConfig.ProfileFolder, $"auto-item-buff-{profileName}.log");
        }

        public string GetConfiguration()
        {
            return JsonConvert.SerializeObject(this);
        }

        public string GetActionName()
        {
            return ACTION_NAME_AUTO_ITEM_BUFF;
        }
    }
}
