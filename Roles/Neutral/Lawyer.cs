using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using static TOHE.Options;

namespace TOHE.Roles.Neutral;

public static class Lawyer
{
    private static readonly int Id = 650700;
    public static List<byte> playerIdList = new();
    public static byte WinnerID;

    private static OptionItem CanTargetImpostor;
    private static OptionItem CanTargetNeutralKiller;
    private static OptionItem CanTargetCrewmate;
    public static OptionItem ChangeRolesAfterTargetKilled;


    /// <summary>
    /// Key: エクスキューショナーのPlayerId, Value: ターゲットのPlayerId
    /// </summary>
    public static Dictionary<byte, byte> Target = new();
    public static readonly string[] ChangeRoles =
    {
        CustomRoles.Crewmate.ToString(), CustomRoles.Jester.ToString(), CustomRoles.Opportunist.ToString(),
    };
    public static readonly CustomRoles[] CRoleChangeRoles =
    {
        CustomRoles.Crewmate, CustomRoles.Jester, CustomRoles.Opportunist,
    };

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.ExclusiveRoles, CustomRoles.Lawyer);
        CanTargetImpostor = BooleanOptionItem.Create(Id + 10, "LawyerCanTargetImpostor", true, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Lawyer]);
        CanTargetNeutralKiller = BooleanOptionItem.Create(Id + 12, "LawyerCanTargetNeutralKiller", false, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Lawyer]);
        CanTargetCrewmate = BooleanOptionItem.Create(Id + 13, "LawyerCanTargetCrewmate", false, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Lawyer]);
        ChangeRolesAfterTargetKilled = StringOptionItem.Create(Id + 11, "LawyerChangeRolesAfterTargetKilled", ChangeRoles, 1, TabGroup.ExclusiveRoles, false).SetParent(CustomRoleSpawnChances[CustomRoles.Lawyer]);
    }
    public static void Init()
    {
        playerIdList = new();
        Target = new();
    }
    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);

        //ターゲット割り当て
        if (AmongUsClient.Instance.AmHost)
        {
            List<PlayerControl> targetList = new();
            var rand = IRandom.Instance;
            foreach (var target in Main.AllPlayerControls)
            {
                if (playerId == target.PlayerId) continue;
                else if (!CanTargetImpostor.GetBool() && target.Is(CustomRoleTypes.Impostor)) continue;
                else if (!CanTargetNeutralKiller.GetBool() && target.IsNeutralKiller()) continue;
                else if (!CanTargetCrewmate.GetBool() && target.Is(CustomRoleTypes.Crewmate)) continue;
                else if (target.Is(CustomRoleTypes.Neutral) && !target.IsNeutralKiller()) continue;
                if (target.GetCustomRole() is CustomRoles.GM or CustomRoles.SuperStar) continue;
                if (Utils.GetPlayerById(playerId).Is(CustomRoles.Lovers) && target.Is(CustomRoles.Lovers)) continue;

                targetList.Add(target);
            }
            var SelectedTarget = targetList[rand.Next(targetList.Count)];
            Target.Add(playerId, SelectedTarget.PlayerId);
            SendRPC(playerId, SelectedTarget.PlayerId, "SetTarget");
            Logger.Info($"{Utils.GetPlayerById(playerId)?.GetNameWithRole()}:{SelectedTarget.GetNameWithRole()}", "Lawyer");
        }
    }
    public static bool IsEnable() => playerIdList.Count > 0;
    public static void SendRPC(byte lawyerId, byte targetId = 0x73, string Progress = "")
    {
        MessageWriter writer;
        switch (Progress)
        {
            case "SetTarget":
                writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetLawyerTarget, SendOption.Reliable);
                writer.Write(lawyerId);
                writer.Write(targetId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                break;
            case "":
                if (!AmongUsClient.Instance.AmHost) return;
                writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.RemoveLawyerTarget, SendOption.Reliable);
                writer.Write(lawyerId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                break;
            
        }
    }
    public static void ReceiveRPC(MessageReader reader, bool SetTarget)
    {
        if (SetTarget)
        {
            byte LawyerId = reader.ReadByte();
            byte TargetId = reader.ReadByte();
            Target[LawyerId] = TargetId;
        }
        else
            Target.Remove(reader.ReadByte());
    }
    public static void ChangeRoleByTarget(PlayerControl target)
    {
        byte Lawyer = 0x73;
        Target.Do(x =>
        {
            if (x.Value == target.PlayerId)
                Lawyer = x.Key;
        });
        Utils.GetPlayerById(Lawyer).RpcSetCustomRole(CRoleChangeRoles[ChangeRolesAfterTargetKilled.GetValue()]);
        Target.Remove(Lawyer);
        SendRPC(Lawyer);
        Utils.NotifyRoles();
    }
    public static void ChangeRole(PlayerControl lawyer)
    {
        lawyer.RpcSetCustomRole(CRoleChangeRoles[ChangeRolesAfterTargetKilled.GetValue()]);
        Target.Remove(lawyer.PlayerId);
        SendRPC(lawyer.PlayerId);
        var text = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Lawyer), Translator.GetString(""));
        text = string.Format(text, Utils.ColorString(Utils.GetRoleColor(CRoleChangeRoles[ChangeRolesAfterTargetKilled.GetValue()]), Translator.GetString(CRoleChangeRoles[ChangeRolesAfterTargetKilled.GetValue()].ToString())));
        lawyer.Notify(text);
    }
    public static string TargetMark(PlayerControl seer, PlayerControl target)
    {
        if (!seer.Is(CustomRoles.Lawyer)) return ""; //エクスキューショナー以外処理しない

        var GetValue = Target.TryGetValue(seer.PlayerId, out var targetId);
        return GetValue && targetId == target.PlayerId ? Utils.ColorString(Utils.GetRoleColor(CustomRoles.Lawyer), "♦") : "";
    }
    public static bool CheckExileTarget(GameData.PlayerInfo exiled, bool DecidedWinner, bool Check = false)
    {
        foreach (var kvp in Target.Where(x => x.Value == exiled.PlayerId))
        {
            var lawyer = Utils.GetPlayerById(kvp.Key);
            if (lawyer == null || !lawyer.IsAlive() || lawyer.Data.Disconnected) continue;
            return true;
        }
        return false;
    }
}