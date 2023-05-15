using Hazel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static TOHE.Options;
using static TOHE.Translator;
using UnityEngine;

namespace TOHE.Roles.Neutral;

public static class HexMaster
{
    public enum SwitchTrigger
    {
        Kill,
        Vent,
        DoubleTrigger,
    };
    public static readonly string[] SwitchTriggerText =
    {
        "TriggerKill", "TriggerVent","TriggerDouble"
    };

    private static readonly int Id = 155500;
    private static Color RoleColorHex = Utils.GetRoleColor(CustomRoles.HexMaster);
    private static Color RoleColorImp = Utils.GetRoleColor(CustomRoles.Impostor);

    public static List<byte> playerIdList = new();

    public static Dictionary<byte, bool> HexMode = new();
    public static Dictionary<byte, List<byte>> HexedPlayer = new();

    public static OptionItem ModeSwitchAction;
    public static OptionItem HexesLookLikeSpells;
    public static SwitchTrigger NowSwitchTrigger;
    public static void SetupCustomOption()
    {
        SetupSingleRoleOptions(Id, TabGroup.ExclusiveRoles, CustomRoles.HexMaster, 1, zeroOne: false);        
        ModeSwitchAction = StringOptionItem.Create(Id + 10, "WitchModeSwitchAction", SwitchTriggerText, 2, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.HexMaster]);
        HexesLookLikeSpells = BooleanOptionItem.Create(Id + 11, "HexesLookLikeSpells",  false, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.HexMaster]);
    }
    public static void Init()
    {
        playerIdList = new();
        HexMode = new();
        HexedPlayer = new();
    }
    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);
        HexMode.Add(playerId, false);
        HexedPlayer.Add(playerId, new());
        NowSwitchTrigger = (SwitchTrigger)ModeSwitchAction.GetValue();
        var pc = Utils.GetPlayerById(playerId);
        pc.AddDoubleTrigger();

    }
    public static bool IsEnable => playerIdList.Count > 0;
    private static void SendRPC(bool doHex, byte hexId, byte target = 255)
    {
        if (doHex)
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.DoHex, SendOption.Reliable, -1);
            writer.Write(hexId);
            writer.Write(target);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        else
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetKillOrHex, SendOption.Reliable, -1);
            writer.Write(hexId);
            writer.Write(HexMode[hexId]);
            AmongUsClient.Instance.FinishRpcImmediately(writer);

        }
    }

    public static void ReceiveRPC(MessageReader reader, bool doHex)
    {
        if (doHex)
        {
            var hexmaster = reader.ReadByte();
            var hexedId = reader.ReadByte();
            if (hexedId != 255)
            {
                HexedPlayer[hexmaster].Add(hexedId);
            }
            else
            {
                HexedPlayer[hexmaster].Clear();
            }
        }
        else
        {
            byte playerId = reader.ReadByte();
            HexMode[playerId] = reader.ReadBoolean();
        }
    }
    public static bool IsHexMode(byte playerId)
    {
        return HexMode[playerId];
    }
    public static void SwitchHexMode(byte playerId, bool kill)
    {
        bool needSwitch = false;
        switch (NowSwitchTrigger)
        {
            case SwitchTrigger.Kill:
                needSwitch = kill;
                break;
            case SwitchTrigger.Vent:
                needSwitch = !kill;
                break;
        }
        if (needSwitch)
        {
            HexMode[playerId] = !HexMode[playerId];
            SendRPC(false, playerId);
            Utils.NotifyRoles(SpecifySeer: Utils.GetPlayerById(playerId));
        }
    }
    public static bool HaveHexedPlayer()
    {
        foreach (var hexmaster in playerIdList)
        {
            if (HexedPlayer[hexmaster].Count != 0)
            {
                return true;
            }
        }
        return false;

    }
    public static bool IsHexed(byte target)
    {
        foreach (var hexmaster in playerIdList)
        {
            if (HexedPlayer[hexmaster].Contains(target))
            {
                return true;
            }
        }
        return false;
    }
    public static void SetHexed(PlayerControl killer, PlayerControl target)
    {
        if (!IsHexed(target.PlayerId))
        {
            HexedPlayer[killer.PlayerId].Add(target.PlayerId);
            SendRPC(true, killer.PlayerId, target.PlayerId);
            //キルクールの適正化
            killer.SetKillCooldown();
        }
    }
    public static void RemoveHexedPlayer()
    {
        foreach (var hexmaster in playerIdList)
        {
            HexedPlayer[hexmaster].Clear();
            SendRPC(true, hexmaster);
        }
    }
    public static bool OnCheckMurder(PlayerControl killer, PlayerControl target)
    {
        if (NowSwitchTrigger == SwitchTrigger.DoubleTrigger)
        {
            return killer.CheckDoubleTrigger(target, () => { SetHexed(killer, target); });
        }
        if (!IsHexMode(killer.PlayerId))
        {
            SwitchHexMode(killer.PlayerId, true);
            //キルモードなら通常処理に戻る
            return true;
        }
        SetHexed(killer, target);

        //スペルに失敗してもスイッチ判定
        SwitchHexMode(killer.PlayerId, true);
        //キル処理終了させる
        return false;
    }
    public static void OnCheckForEndVoting(PlayerState.DeathReason deathReason, params byte[] exileIds)
    {
        if (!IsEnable || deathReason != PlayerState.DeathReason.Vote) return;
        foreach (var id in exileIds)
        {
            if (HexedPlayer.ContainsKey(id))
                HexedPlayer[id].Clear();
        }
        var hexedIdList = new List<byte>();
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            var dic = HexedPlayer.Where(x => x.Value.Contains(pc.PlayerId));
            if (dic.Count() == 0) continue;
            var whichId = dic.FirstOrDefault().Key;
            var hexmaster = Utils.GetPlayerById(whichId);
            if (hexmaster != null && hexmaster.IsAlive())
            {
                if (!Main.AfterMeetingDeathPlayers.ContainsKey(pc.PlayerId))
                {
                    pc.SetRealKiller(hexmaster);
                    hexedIdList.Add(pc.PlayerId);
                }
            }
            else
            {
                Main.AfterMeetingDeathPlayers.Remove(pc.PlayerId);
            }
        }
        CheckForEndVotingPatch.TryAddAfterMeetingDeathPlayers(PlayerState.DeathReason.Hex, hexedIdList.ToArray());
        RemoveHexedPlayer();
    }
    public static string GetHexedMark(byte target, bool isMeeting)
    {

        if (isMeeting && IsEnable && IsHexed(target))
        {
            if (!HexesLookLikeSpells.GetBool())
            {
            return Utils.ColorString(RoleColorHex, "†");
            }
            if (HexesLookLikeSpells.GetBool())
            {
            return Utils.ColorString(RoleColorImp, "†");
            }
        }
        return "";

    }
    public static string GetHexModeText(PlayerControl hexmaster, bool hud, bool isMeeting = false)
    {
        if (hexmaster == null || isMeeting) return "";

        var str = new StringBuilder();
        if (hud)
        {
            str.Append(GetString("WitchCurrentMode"));
        }
        else
        {
            str.Append($"{GetString("Mode")}:");
        }
        if (NowSwitchTrigger == SwitchTrigger.DoubleTrigger)
        {
            str.Append(GetString("WitchModeDouble"));
        }
        else
        {
            str.Append(IsHexMode(hexmaster.PlayerId) ? GetString("WitchModeHex") : GetString("WitchModeKill"));
        }
        return str.ToString();
    }
    public static void GetAbilityButtonText(HudManager hud)
    {
        if (IsHexMode(PlayerControl.LocalPlayer.PlayerId) && NowSwitchTrigger != SwitchTrigger.DoubleTrigger)
        {
            hud.KillButton.OverrideText($"{GetString("HexButtonText")}");
        }
        else
        {
            hud.KillButton.OverrideText($"{GetString("KillButtonText")}");
        }
    }

    public static void OnEnterVent(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (playerIdList.Contains(pc.PlayerId))
        {
            if (NowSwitchTrigger is SwitchTrigger.Vent)
            {
                SwitchHexMode(pc.PlayerId, false);
            }
        }
    }
}