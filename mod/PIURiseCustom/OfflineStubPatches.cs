using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader.Utils;

namespace PIURiseCustom
{
    /// <summary>
    /// 오프라인 서버 스텁 + 선택적 온라인 응답 캡처.
    /// OfflineMode=true일 때 모든 게임 서버 통신을 로컬에서 차단하고 안전한 스텁 응답을 반환한다.
    /// (DLC 확인·Steam DRM은 건드리지 않으며 게임 서버 대체로만 한정)
    /// </summary>
    internal static class OfflineStubPatches
    {
        private static readonly object _captureLock = new();
        private static string CaptureFilePath => Path.Combine(MelonEnvironment.GameRootDirectory, "MelonLoader", "net_capture.jsonl");

        private static void AppendCapture(string kind, string cmd, string req, string res)
        {
            try
            {
                lock (_captureLock)
                {
                    string safeReq = (req ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
                    string safeRes = (res ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
                    string line = $"{{\"ts\":\"{DateTime.UtcNow:O}\",\"kind\":\"{kind}\",\"cmd\":\"{cmd}\",\"req\":\"{safeReq}\",\"res\":\"{safeRes}\"}}";
                    File.AppendAllText(CaptureFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[NetCapture] 기록 실패: {ex.Message}");
            }
        }

        private static string BuildStubResponse(NetCommand cmd)
        {
            switch (cmd)
            {
                case NetCommand.checkTicketAndAuth:
                    return "{\"dlc\":[],\"token\":\"offline_local_jwt\"}";

                case NetCommand.getMaintenanceSchedule:
                    return "{\"shutdownTime\":\"\",\"resumptionTime\":\"\",\"alramTime\":\"\"}";

                case NetCommand.getGameStartTime:
                    return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

                case NetCommand.heartbeat:
                    return "{\"ok\":true}";

                case NetCommand.getPlayerInven:
                case NetCommand.getPlayerInfos:
                case NetCommand.getRank:
                case NetCommand.getBattleLeaders:
                case NetCommand.getUseSeasonPointList:
                case NetCommand.getDivisionChallengeLog:
                case NetCommand.getPlayerWorldMaxMissionList:
                case NetCommand.getDailyQuest:
                case NetCommand.getSkillUpBossSongs:
                    return "[]";

                case NetCommand.getHighlightOwnList:
                case NetCommand.getHighlightClearList:
                case NetCommand.getPlayerEquips:
                case NetCommand.getSeasonPassInfo:
                case NetCommand.getDivisionInfo:
                case NetCommand.getMyLeaderBoard:
                case NetCommand.getMyBattleScore:
                case NetCommand.getPlayerWorldMaxMCoin:
                    return "{}";

                case NetCommand.getHighlightCurrency:
                    return "{\"GreenH\":0,\"PurpleH\":0,\"greenH\":0,\"purpleH\":0,\"green\":0,\"purple\":0,\"currency\":0}";

                // 기록/로그 전송 계열은 전부 로컬 no-op
                case NetCommand.setRankResult:
                case NetCommand.playLog:
                case NetCommand.setPlayerInfo:
                case NetCommand.setPlayerEquip:
                case NetCommand.addPlayerInven:
                default:
                    return "{}";
            }
        }

        // 1. 부팅 관문: SteamManager.CheckTicket(Action callback)
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.CheckTicket))]
        internal static class SteamManager_CheckTicket_Patch
        {
            private static bool Prefix(Il2CppSystem.Action callback, ref Il2CppSystem.Threading.Tasks.Task __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;

                try
                {
                    Mod.Log.Msg("[OfflineStub] SteamManager.CheckTicket 가로챔 → 로컬 오프라인 JWT 주입");
                    var nm = NetworkManager.Instance;
                    if (nm != null)
                    {
                        var fakeJwt = new JWT
                        {
                            dlc = new Il2CppReferenceArray<DLCEntry>(0),
                            token = "offline_local_jwt"
                        };
                        nm._token = fakeJwt;
                    }

                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[OfflineStub] CheckTicket 스텁 오류: {ex}");
                }

                __result = Il2CppSystem.Threading.Tasks.Task.CompletedTask;
                return false;
            }
        }

        // 2. 주기적 JWT 갱신 코루틴 차단
        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartJwtCoroutine))]
        internal static class NetworkManager_StartJwtCoroutine_Patch
        {
            private static bool Prefix()
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;
                Mod.Log.Msg("[OfflineStub] StartJwtCoroutine 차단");
                return false;
            }
        }

        // 3. 주기적 하트비트 코루틴 차단
        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.SendHeartBeatRoutine))]
        internal static class NetworkManager_SendHeartBeatRoutine_Patch
        {
            private static bool Prefix(ref Il2CppSystem.Collections.IEnumerator __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;
                __result = null;
                return false;
            }
        }

        // 4. 인증 요청 차단 / 캡처
        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.SendAuth))]
        internal static class NetworkManager_SendAuth_Patch
        {
            private static bool Prefix(string content, ref Il2CppSystem.Action<string> callback)
            {
                if (ModConfig.OfflineMode.Value)
                {
                    string stub = BuildStubResponse(NetCommand.checkTicketAndAuth);
                    Mod.Log.Msg($"[OfflineStub] SendAuth 차단 → {stub}");
                    callback?.Invoke(stub);
                    return false;
                }

                if (ModConfig.CaptureOnlineResponses.Value && callback != null)
                {
                    var orig = callback;
                    callback = new Action<string>(res =>
                    {
                        AppendCapture("SendAuth", "checkTicketAndAuth", content, res);
                        orig.Invoke(res);
                    });
                }

                return true;
            }
        }

        // 5. 일반 REST 명령 핵심 진입점: NetworkManager.Send (private — GetGameStartTime/GetMaintenanceSchedule 등이 직접 호출)
        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.Send))]
        internal static class NetworkManager_Send_Patch
        {
            private static bool Prefix(NetCommand command, string content, ref Il2CppSystem.Action<string> callback)
            {
                if (ModConfig.OfflineMode.Value)
                {
                    string stub = BuildStubResponse(command);
                    Mod.Log.Msg($"[OfflineStub] Send({command}) 차단 → {stub}");
                    try
                    {
                        callback?.Invoke(stub);
                    }
                    catch (Exception ex)
                    {
                        if (command != NetCommand.getHighlightCurrency)
                            Mod.Log.Warning($"[OfflineStub] Send({command}) 콜백 처리 중 예외(무시): {ex.Message}");
                    }
                    return false;
                }

                if (ModConfig.CaptureOnlineResponses.Value && callback != null)
                {
                    var orig = callback;
                    string cmdName = command.ToString();
                    callback = new Action<string>(res =>
                    {
                        AppendCapture("Send", cmdName, content, res);
                        orig.Invoke(res);
                    });
                }

                return true;
            }
        }

        // 6. 점검/타이틀 체크 및 네트워크 오류 팝업 우회 (오프라인 모드 전용)
        [HarmonyPatch(typeof(MaintenanceManager), nameof(MaintenanceManager.TitleCheck))]
        internal static class MaintenanceManager_TitleCheck_Patch
        {
            private static bool Prefix(Il2CppSystem.Action callback)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;

                Mod.Log.Msg("[OfflineStub] MaintenanceManager.TitleCheck 우회 → callback 실행");
                callback?.Invoke();
                return false;
            }
        }

        [HarmonyPatch(typeof(MaintenanceManager), nameof(MaintenanceManager.MaintenanceCheck))]
        internal static class MaintenanceManager_MaintenanceCheck_Patch
        {
            private static bool Prefix(Il2CppSystem.Action callback)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;

                Mod.Log.Msg("[OfflineStub] MaintenanceManager.MaintenanceCheck 우회 → callback 실행");
                callback?.Invoke();
                return false;
            }
        }

        [HarmonyPatch(typeof(MaintenanceManager), nameof(MaintenanceManager.AlramNetworkError))]
        internal static class MaintenanceManager_AlramNetworkError_Patch
        {
            private static bool Prefix()
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;

                Mod.Log.Warning("[OfflineStub] MaintenanceManager.AlramNetworkError() 호출 차단됨");
                return false;
            }
        }

        // 7. 인게임 시작/종료 시 서버 플레이 로그 전송 차단 (오프라인 모드 또는 커스텀 곡에서 NullRef 방지)
        [HarmonyPatch(typeof(Il2CppPIUUtil.CommonUtil), nameof(Il2CppPIUUtil.CommonUtil.SetPlayLogAtServer))]
        internal static class CommonUtil_SetPlayLogAtServer_Patch
        {
            private static bool Prefix(string songID)
            {
                if (ModConfig.OfflineMode.Value || CustomPackManager.IsCatalogSongId(songID))
                {
                    Mod.Log.Msg($"[OfflineStub] CommonUtil.SetPlayLogAtServer({songID}) 차단");
                    return false;
                }
                return true;
            }
        }
    }
}
