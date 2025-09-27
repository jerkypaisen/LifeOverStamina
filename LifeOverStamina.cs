using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("LifeOverStamina", "jerky", "1.8.0")]
    [Description("Health drains while climbing (except ladders), then restores after.")]

    public class LifeOverStamina : RustPlugin
    {
        private float healthLossPerSecond = 2.0f;
        private float recoveryRatePerTick = 3.0f;
        private float recoveryDelay = 3.0f;

        private Dictionary<ulong, float> recoveryTargetHealth = new Dictionary<ulong, float>();
        private Dictionary<ulong, float> recoveryStartTime = new Dictionary<ulong, float>();

        #region Config
        protected override void LoadDefaultConfig()
        {
            Config["HealthLossPerSecond"] = healthLossPerSecond;
            Config["RecoveryRatePerTick"] = recoveryRatePerTick;
            Config["RecoveryDelay"] = recoveryDelay;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            healthLossPerSecond = GetConfig("HealthLossPerSecond", 2.0f);
            recoveryRatePerTick = GetConfig("RecoveryRatePerTick", 3.0f);
            recoveryDelay = GetConfig("RecoveryDelay", 3.0f);
        }

        private T GetConfig<T>(string key, T defaultValue)
        {
            if (Config[key] is T value) return value;
            return defaultValue;
        }
        #endregion

        private void Init()
        {
            timer.Every(0.2f, TickLoop);
        }

        private void TickLoop()
        {
            float now = Time.realtimeSinceStartup;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsAlive() || player.IsSleeping() || player.IsWounded())
                    continue;

                bool climbing = player.OnLadder();
                bool onLadderItem = false;

                var trigger = player.FindTrigger<TriggerLadder>();
                if (climbing && trigger != null)
                {
                    var entity = trigger.GetComponentInParent<BaseEntity>();
                    if (entity != null)
                    {
                        string prefab = entity.PrefabName ?? entity.ShortPrefabName;
                        if (prefab.Contains("ladder.wooden.wall"))
                        {
                            onLadderItem = true;
                        }
                    }
                }

                if (climbing && !onLadderItem)
                {
                    // 登攀開始時に回復目標を記録（既存があれば保持）
                    if (!recoveryTargetHealth.ContainsKey(player.userID))
                    {
                        recoveryTargetHealth[player.userID] = player.health;
                    }

                    // 登攀中は回復タイマーをリセット
                    recoveryStartTime[player.userID] = -1f;

                    // ヘルス減少
                    float loss = healthLossPerSecond * 0.2f;
                    float newHealth = Mathf.Max(1f, player.health - loss);
                    player.SetHealth(newHealth);
                    player.SendNetworkUpdate();


                    // ヘルスが1になったら ragdoll
                    if (newHealth <= 1f)
                    {
                        player.Ragdoll();
                    }
                }
                else
                {
                    // 登攀終了時に回復タイマーをセット（未設定なら）
                    if (recoveryTargetHealth.ContainsKey(player.userID))
                    {
                        if (!recoveryStartTime.ContainsKey(player.userID) || recoveryStartTime[player.userID] < 0f)
                        {
                            recoveryStartTime[player.userID] = now + recoveryDelay;
                        }

                        // 回復開始タイミングをチェック
                        if (now >= recoveryStartTime[player.userID])
                        {
                            float target = recoveryTargetHealth[player.userID];
                            if (player.health < target)
                            {
                                float newHealth = Mathf.Min(target, player.health + recoveryRatePerTick);
                                player.SetHealth(newHealth);
                                player.SendNetworkUpdate();
                            }
                            else
                            {
                                recoveryTargetHealth.Remove(player.userID);
                                recoveryStartTime.Remove(player.userID);
                            }
                        }
                    }
                }
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = entity as BasePlayer;
            if (player == null || !player.IsAlive()) return;

            // ダメージを受けたら回復目標を現在のヘルスに更新
            recoveryTargetHealth[player.userID] = player.health;
            recoveryStartTime[player.userID] = -1f;
        }
    }
}
