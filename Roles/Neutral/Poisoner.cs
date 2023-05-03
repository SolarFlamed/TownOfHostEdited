using AmongUs.GameOptions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE.Roles.Impostor;

public static class Poisoner
{
    private class PoisonedInfo
    {
        public byte PoisonerId;
        public float KillTimer;

        public PoisonedInfo(byte poisonerId, float killTimer)
        {
            PoisonerId = poisonerId;
            KillTimer = killTimer;
        }
    }

    private static readonly int Id = 51300;
    private static readonly List<byte> PlayerIdList = new();
    private static OptionItem OptionKillDelay;
    private static float KillDelay;
    public static OptionItem CanVent;
    public static OptionItem KillCooldown;
    private static readonly Dictionary<byte, PoisonedInfo> PoisonedPlayers = new();
    public static void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.Poisoner);
        KillCooldown = FloatOptionItem.Create(Id + 10, "PoisonCooldown", new(0f, 180f, 2.5f), 20f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner])
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillDelay = FloatOptionItem.Create(Id + 11, "PoisonerKillDelay", new(1f, 999f, 1f), 10f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 12, "CanVent", true, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner]);
    }
    public static void Init()
    {
        IsEnable = false;
        PlayerIdList.Clear();
        PoisonedPlayers.Clear();

        KillDelay = OptionKillDelay.GetFloat();
    }

    public static void Add(byte playerId)
    {
        IsEnable = true;
        PlayerIdList.Add(playerId);
    }

    public static bool IsEnable = false;
    public static bool IsThisRole(byte playerId) => PlayerIdList.Contains(playerId);
    public static void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = KillCooldown.GetFloat();

    public static bool OnCheckMurder(PlayerControl killer, PlayerControl target)
    {
        if (!IsThisRole(killer.PlayerId)) return true;
        if (target.Is(CustomRoles.Bait)) return true;

        killer.SetKillCooldown();

        //誰かに噛まれていなければ登録
        if (!PoisonedPlayers.ContainsKey(target.PlayerId))
        {
            PoisonedPlayers.Add(target.PlayerId, new(killer.PlayerId, 0f));
        }
        return false;
    }

    public static void OnFixedUpdate(PlayerControl poisoner)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return;

        var poisonerID = poisoner.PlayerId;
        if (!IsThisRole(poisoner.PlayerId)) return;

        List<byte> targetList = new(PoisonedPlayers.Where(b => b.Value.PoisonerId == poisonerID).Select(b => b.Key));

        foreach (var targetId in targetList)
        {
            var poisonedPoisoner = PoisonedPlayers[targetId];
            if (poisonedPoisoner.KillTimer >= KillDelay)
            {
                var target = Utils.GetPlayerById(targetId);
                KillPoisoned(poisoner, target);
                PoisonedPlayers.Remove(targetId);
            }
            else
            {
                poisonedPoisoner.KillTimer += Time.fixedDeltaTime;
                PoisonedPlayers[targetId] = poisonedPoisoner;
            }
        }
    }
    public static void KillPoisoned(PlayerControl poisoner, PlayerControl target, bool isButton = false)
    {
        if (poisoner == null || target == null || target.Data.Disconnected) return;
        if (target.IsAlive())
        {
            Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Poison;
            target.SetRealKiller(poisoner);
            target.RpcMurderPlayerV3(target);
            Logger.Info($"Poisonerに噛まれている{target.name}を自爆させました。", "Poisoner");
            if (!isButton && poisoner.IsAlive())
            {
                RPC.PlaySoundRPC(poisoner.PlayerId, Sounds.KillSound);
                if (target.Is(CustomRoles.Trapper))
                    poisoner.TrapperKilled(target);
                poisoner.Notify(GetString("PoisonerTargetDead"));
            }
        }
        else
        {
            Logger.Info("Poisonerに噛まれている" + target.name + "はすでに死んでいました。", "Poisoner");
        }
    }
    public static void ApplyGameOptions(IGameOptions opt) => opt.SetVision(true);

    public static void OnStartMeeting()
    {
        foreach (var targetId in PoisonedPlayers.Keys)
        {
            var target = Utils.GetPlayerById(targetId);
            var poisoner = Utils.GetPlayerById(PoisonedPlayers[targetId].PoisonerId);
            KillPoisoned(poisoner, target);
        }
        PoisonedPlayers.Clear();
    }
    public static void SetKillButtonText()
    {
        HudManager.Instance.KillButton.OverrideText($"{GetString("PoisonerPoisonButtonText")}");
    }
}
