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
                    // 예전 수동 이스케이프는 역슬래시·제어문자를 처리하지 않아 JSONL이 깨졌다 → 표준 JSON 문자열 인코딩 사용
                    string line = "{\"ts\":" + JsonString(DateTime.UtcNow.ToString("O")) +
                                  ",\"kind\":" + JsonString(kind) +
                                  ",\"cmd\":" + JsonString(cmd) +
                                  ",\"req\":" + JsonString(req) +
                                  ",\"res\":" + JsonString(res) + "}";
                    File.AppendAllText(CaptureFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[NetCapture] 기록 실패: {ex.Message}");
            }
        }

        private static string JsonString(string s)
        {
            if (s == null) return "null";
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>
        /// 오프라인에서는 로그인 응답(JWT의 dlc 목록)이 없어 `PlayerData.Instance.DLCEntrys`(@0x80)가 null로 남는다.
        /// 멀티 방 생성 창(MultiStageCreateRoomPopup.SetSongInit)이 이 배열을 순회하다 NullReferenceException으로 죽고,
        /// 그 예외로 호출한 DoCreateRoom이 중단되어 창의 키 등록(SetKeyAction)이 빠져 ESC/ENTER가 먹통이 된다(2026-11-23 실기).
        /// → null이면 빈 배열(= DLC 없음)로 채운다. 여러 지점에서 불러도 안전(이미 있으면 아무것도 안 함).
        /// </summary>
        internal static void EnsureOfflineDlcEntries(string where)
        {
            try
            {
                if (!ModConfig.OfflineMode.Value)
                    return;

                var pd = PlayerData.Instance;
                if (pd != null && pd.DLCEntrys == null)
                {
                    pd.DLCEntrys = new Il2CppReferenceArray<DLCEntry>(0);
                    Mod.Log.Msg($"[OfflineStub] PlayerData.DLCEntrys가 null이라 빈 배열로 채움 ({where})");
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Warning($"[OfflineStub] DLCEntrys 보강 실패({where}): {ex.Message}");
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
                case NetCommand.setDivisionInfo:
                case NetCommand.setUseSeasonPoint:
                case NetCommand.setSeasonPassInfo:
                case NetCommand.setBattlePointInfo:
                case NetCommand.setWorldMaxMissionTry:
                case NetCommand.setWorldMaxMissionClear:
                case NetCommand.setHighlightClear:
                case NetCommand.setBossClear:
                case NetCommand.updateDailyQuestProgress:
                    return "{}";

                // 아직 실제 응답 형식을 모르는 명령: 빈 객체로 응답하되 처음 한 번만 경고해 다음 실기에서 눈에 띄게 한다
                default:
                    NoteUnmodeled(cmd);
                    return "{}";
            }
        }

        private static bool IsResultSubmission(NetCommand cmd)
        {
            switch (cmd)
            {
                case NetCommand.setRankResult:
                case NetCommand.playLog:
                case NetCommand.setHighlightClear:
                case NetCommand.setBossClear:
                case NetCommand.setWorldMaxMissionTry:
                case NetCommand.setWorldMaxMissionClear:
                case NetCommand.setBattlePointInfo:
                case NetCommand.setDivisionInfo:
                case NetCommand.updateDailyQuestProgress:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldWithholdResult(out string why)
        {
            why = null;
            try
            {
                if (Util.IsCurrentSongCustom()) why = "커스텀 곡";
                else if (SaveCustomConfig.AutoPlay) why = "AutoPlay";
                else if (SaveCustomConfig.AllPerfect) why = "AllPerfect";
            }
            catch { why = "판정 실패(안전하게 보류)"; }
            return why != null;
        }

        private static readonly System.Collections.Generic.HashSet<NetCommand> _unmodeledSeen = new();
        private static readonly System.Collections.Generic.HashSet<NetCommand> _sendSeen = new();

        private static bool FirstSeen(NetCommand cmd)
        {
            lock (_sendSeen) return _sendSeen.Add(cmd);
        }

        private static void NoteUnmodeled(NetCommand cmd)
        {
            bool first;
            lock (_unmodeledSeen) first = _unmodeledSeen.Add(cmd);
            if (first)
                Mod.Log.Warning($"[OfflineStub] 응답 형식 미확인 명령 {cmd} → 빈 객체 {{}} 반환 (이 명령 직후 화면이 이상하면 응답 캡처 필요)");
        }

        // 0-1. 부팅 관문: SteamManager.InitManager(Action callback)
        // 오프라인 모드일 때 IsUseSteam을 false로 설정하여
        // SteamAPI_RestartAppIfNecessary(Application.Quit) 및 Steamworks.SteamClient.Init을 원천 건너뛰고
        // Steam 클라이언트 미실행 상태에서도 정상 부팅되도록 보장한다.
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.InitManager))]
        internal static class SteamManager_InitManager_Patch
        {
            private static void Prefix(SteamManager __instance)
            {
                if (!ModConfig.OfflineMode.Value || __instance == null)
                    return;

                try
                {
                    __instance.IsUseSteam = false;
                    Mod.Log.Msg("[OfflineStub] SteamManager.InitManager: IsUseSteam = false 설정 (Steam 재시작/인증 의존성 원천 우회)");
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[OfflineStub] InitManager Prefix 예외: {ex}");
                }
            }
        }

        // 0-2. 스팀 사용 여부 질의 우회
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.CheckUseSteam))]
        internal static class SteamManager_CheckUseSteam_Patch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.GetIsUseSteam))]
        internal static class SteamManager_GetIsUseSteam_Patch
        {
            private static bool Prefix(ref bool __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;
                __result = false;
                return false;
            }
        }

        // 0-3. DLC 상태 전수 등록 (Steamworks API 없이 11개 공식 DLC 직접 등록)
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.CheckAllDLCStatus))]
        internal static class SteamManager_CheckAllDLCStatus_Patch
        {
            private static readonly uint[] _allDlcs = {
                5344930, 3932570, 4445930, 4448630, 4448870,
                4528900, 4744660, 4859200, 4859210, 5344920, 4446060
            };

            private static bool Prefix(SteamManager __instance)
            {
                if (!ModConfig.OfflineMode.Value || __instance == null)
                    return true;

                try
                {
                    var list = new Il2CppSystem.Collections.Generic.List<uint>();
                    foreach (var id in _allDlcs)
                    {
                        list.Add(id);
                    }
                    __instance.dlcOwnedList = list;
                    Mod.Log.Msg("[OfflineStub] SteamManager.CheckAllDLCStatus 가로챔: 11개 공식 DLC 소유권 등록");
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[OfflineStub] CheckAllDLCStatus 예외: {ex}");
                }
                return false;
            }
        }

        // 0-4. DLC 설치/소유 질의 무조건 true
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.IsDLCInstalled))]
        internal static class SteamManager_IsDLCInstalled_Patch
        {
            private static bool Prefix(uint appID, ref bool __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;
                __result = true;
                return false;
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
                    EnsureOfflineDlcEntries("CheckTicket");
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
                    // 같은 명령이 씬 전환마다 반복되므로 명령별 첫 1회만 Msg로 남긴다(v0.3.3, 로그 소음 감소)
                    if (FirstSeen(command))
                        Mod.Log.Msg($"[OfflineStub] Send({command}) 차단 → {stub} (이후 같은 명령은 기록 생략)");
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

                // 온라인 모드여도 커스텀 곡·오토플레이·올퍼펙트 플레이의 결과/기록 전송은 서버로 보내지 않는다.
                // (커스텀 곡은 템플릿 공식 곡을 거쳐 시작하므로 결과가 공식 곡 기록이나 랭킹에 섞일 수 있다)
                if (IsResultSubmission(command) && ShouldWithholdResult(out string why))
                {
                    if (FirstSeen(command))
                        Mod.Log.Warning($"[OnlineGuard] Send({command}) 서버 전송 보류({why}) → 로컬 {{}} 응답");
                    try { callback?.Invoke("{}"); }
                    catch (Exception ex) { Mod.Log.Warning($"[OnlineGuard] Send({command}) 콜백 처리 중 예외(무시): {ex.Message}"); }
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

        // 8. 멀티플레이 WebSocket(ws://riselive.piugame.com:3011) 외부 접속 차단 (v0.3.2)
        //    원본 NetworkManager.InitMultiCon()은 `_mc`가 null이면 MultiplayController를 만들고 Init()·SetHost("ws://riselive…")를 한 뒤,
        //    `_mc`가 연결 상태가 아니면 ConnectAsync로 소켓을 연다(멀티 메뉴 F2에서 불린다).
        //    → 오프라인 모드에서는 원본보다 먼저 `_mc`를 접속 대상 127.0.0.1:1(아무도 듣지 않는 로컬 포트)로 만들어 넣는다.
        //      원본은 `_mc`가 이미 있으니 호스트 설정을 건너뛰고 ConnectAsync를 부르지만, 로컬에서 즉시 거부되어 외부 통신이 0이고
        //      게임이 "접속 실패" 경로(ConnectAsync 안의 catch → OnError 알림)를 스스로 처리한다.
        //    ★ 시도했다가 실패한 것 두 가지:
        //      (1) MultiplayController.ConnectAsync(string, CancellationToken) 직접 후킹 → struct 인자(CancellationToken)를 Il2CppInterop 트램펄린이
        //          박싱하다 il2cpp_value_box에서 AccessViolationException으로 게임이 죽는다(2026-11-23 실기 크래시).
        //      (2) InitMultiCon이 끝나지 않는 Task(Task.Delay(-1))를 돌려주게 막기 → CreateRoom 등이 `await InitMultiCon()`에서 영원히 대기해
        //          방 생성 창의 ESC/ENTER가 먹통이 된다(2026-11-23 실기).
        private const string OfflineMultiHost = "ws://127.0.0.1:1";
        private static bool _multiBlockLogged;

        [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.InitMultiCon))]
        internal static class NetworkManager_InitMultiCon_Patch
        {
            private static bool Prefix(NetworkManager __instance, ref Il2CppSystem.Threading.Tasks.Task __result)
            {
                if (!ModConfig.OfflineMode.Value)
                    return true;

                try
                {
                    EnsureOfflineDlcEntries("InitMultiCon");

                    if (__instance._mc == null)
                    {
                        var mc = new MultiplayController();
                        mc.Init();
                        mc.SetHost(OfflineMultiHost);
                        __instance._mc = mc;

                        if (!_multiBlockLogged)
                        {
                            _multiBlockLogged = true;
                            Mod.Log.Msg($"[OfflineStub] NetworkManager.InitMultiCon: 멀티 접속 대상을 {OfflineMultiHost}(로컬, 즉시 실패)로 교체 — 외부 서버 접속 안 함");
                        }
                    }
                    return true; // 원본 실행: 접속은 로컬에서 거부되고 게임이 접속 실패를 스스로 처리한다
                }
                catch (Exception ex)
                {
                    // `_mc`를 못 만들었는데 원본을 실행하면 실제 서버로 접속하므로, 이 경우에는 접속을 보류 상태로 막는다
                    Mod.Log.Error($"[OfflineStub] InitMultiCon 로컬 대체 실패 → 접속 보류로 폴백: {ex.Message}");
                    __result = Il2CppSystem.Threading.Tasks.Task.Delay(-1);
                    return false;
                }
            }
        }

        // 7. 인게임 시작/종료 시 서버 플레이 로그 전송 차단 (오프라인 모드, 커스텀 곡, 또는 AutoPlay/AllPerfect 활성 시)
        [HarmonyPatch(typeof(Il2CppPIUUtil.CommonUtil), nameof(Il2CppPIUUtil.CommonUtil.SetPlayLogAtServer))]
        internal static class CommonUtil_SetPlayLogAtServer_Patch
        {
            private static bool Prefix(string songID)
            {
                try
                {
                    if (ModConfig.OfflineMode.Value)
                    {
                        Mod.Log.Msg($"[OfflineStub] CommonUtil.SetPlayLogAtServer({songID}) 차단");
                        return false;
                    }
                    if (CustomPackManager.IsCatalogSongId(songID) || ShouldWithholdResult(out _))
                    {
                        Mod.Log.Warning($"[OnlineGuard] CommonUtil.SetPlayLogAtServer({songID}) 보류 (커스텀 곡 또는 AutoPlay/AllPerfect)");
                        return false;
                    }
                }
                catch
                {
                    // 판정 실패 시 안전하게 전송 보류
                    return false;
                }
                return true;
            }
        }
    }
}
