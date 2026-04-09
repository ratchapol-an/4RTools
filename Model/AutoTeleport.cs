using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    public class AutoTeleport : Action
    {
        public const string ACTION_NAME_AUTO_TELEPORT = "AutoTeleport";
        public static event System.Action<string> TeleportLogged;

        public Key flyWingKey { get; set; } = Key.None;
        public int hpPercent { get; set; } = 50;
        public int checkHpIntervalMs { get; set; } = 20;
        public int cooldownMs { get; set; } = 1200;
        public bool isEnabled { get; set; } = false;
        public string toggleKey { get; set; } = Keys.Next.ToString(); // PgDown

        private _4RThread thread;
        private long cooldownUntilTicksUtc;
        private uint hpWhenTeleportTriggered;

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                cooldownUntilTicksUtc = 0;
                hpWhenTeleportTriggered = 0;

                this.thread = new _4RThread(_ => AutoTeleportThreadExecution(roClient));
                _4RThread.Start(this.thread);
            }
        }

        public void Stop()
        {
            _4RThread.Stop(this.thread);
        }

        private int AutoTeleportThreadExecution(Client roClient)
        {
            uint currentHp = roClient.ReadCurrentHp();
            uint maxHp = roClient.ReadMaxHp();

            // If the memory pointer is unavailable/invalid, avoid false teleports.
            if (maxHp == 0)
            {
                System.Threading.Thread.Sleep(GetSafeInterval());
                return 0;
            }

            long nowTicks = DateTime.UtcNow.Ticks;
            long untilTicks = System.Threading.Interlocked.Read(ref cooldownUntilTicksUtc);
            if (nowTicks < untilTicks)
            {
                // During cooldown, keep monitoring: if HP recovers we do not retrigger.
                if (currentHp > hpWhenTeleportTriggered)
                {
                    System.Threading.Thread.Sleep(GetSafeInterval());
                    return 0;
                }

                System.Threading.Thread.Sleep(GetSafeInterval());
                return 0;
            }

            bool hpBelowThreshold = currentHp * 100 < (uint)hpPercent * maxHp;
            if (hpBelowThreshold)
            {
                Teleport(currentHp, maxHp);
                hpWhenTeleportTriggered = currentHp;
                long newUntilTicks = DateTime.UtcNow.AddMilliseconds(GetSafeCooldown()).Ticks;
                System.Threading.Interlocked.Exchange(ref cooldownUntilTicksUtc, newUntilTicks);
            }

            System.Threading.Thread.Sleep(GetSafeInterval());
            return 0;
        }

        private void Teleport(uint currentHp, uint maxHp)
        {
            Keys key = (Keys)Enum.Parse(typeof(Keys), flyWingKey.ToString());
            if (key == Keys.None || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                return;
            }

            IntPtr target = ClientSingleton.GetClient().process.MainWindowHandle;
            bool keySent = InputCoordinator.TrySendHighPriorityKey(target, key);
            if (!keySent)
            {
                return;
            }

            int hpPct = maxHp == 0 ? 0 : (int)((currentHp * 100) / maxHp);
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Teleport triggered. HP={currentHp}/{maxHp} ({hpPct}%), threshold={hpPercent}%, cooldown={GetSafeCooldown()}ms.";
            AppendLog(logLine);
            TeleportLogged?.Invoke(logLine);
        }

        private void AppendLog(string logLine)
        {
            string path = GetLogFilePath();
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.AppendAllText(path, logLine + Environment.NewLine);
        }

        private string GetLogFilePath()
        {
            string profileName = ProfileSingleton.GetCurrent()?.Name ?? "default";
            return Path.Combine(AppConfig.ProfileFolder, $"auto-teleport-{profileName}.log");
        }

        private int GetSafeInterval()
        {
            return Math.Max(10, checkHpIntervalMs);
        }

        private int GetSafeCooldown()
        {
            return Math.Max(100, cooldownMs);
        }

        public string GetConfiguration()
        {
            return JsonConvert.SerializeObject(this);
        }

        public string GetActionName()
        {
            return ACTION_NAME_AUTO_TELEPORT;
        }
    }
}
