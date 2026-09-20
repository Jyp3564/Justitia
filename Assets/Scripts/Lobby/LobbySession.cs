using System;

namespace Justitia
{
    public enum LobbyPhase { Preparing, HostSetup, Submitted }

    [Serializable]
    public sealed class LobbySnapshot
    {
        public string sessionId;
        public long revision;
        public LobbyPhase phase;
        public bool hostReady;
        public bool guestReady;
        public string topic;
        public int maxRound;
        public string mapId;
    }

    // Only the host mutates the model. Guests apply validated, versioned snapshots.
    public sealed class LobbySession
    {
        public LobbyPhase Phase { get; private set; }
        public bool HostReady { get; private set; }
        public bool GuestReady { get; private set; }
        public string Topic { get; private set; } = "";
        public int MaxRound { get; private set; } = 3;
        public string MapId { get; private set; }="";
        public bool SelectMap(PlayerRole role,string id)
        {
            if(role!=PlayerRole.Host || Phase!=LobbyPhase.Preparing || id!="court")return false;
            if(MapId==id)return true;
            MapId=id;HostReady=GuestReady=false;Revision++;Changed?.Invoke();return true;
        }
        public event Action Changed;
        public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
        public long Revision { get; private set; }

        public bool SetReady(PlayerRole role, bool ready)
        {
            if (Phase != LobbyPhase.Preparing || !ValidRole(role) || MapId!="court") return false;
            if (role == PlayerRole.Host) HostReady = ready; else GuestReady = ready;
            if (HostReady && GuestReady) Phase = LobbyPhase.HostSetup;
            Revision++;
            Changed?.Invoke();
            return true;
        }

        public bool Submit(PlayerRole role, string topic, string rounds, out string error)
        {
            error = "";
            if (role != PlayerRole.Host) error = "Host만 설정을 제출할 수 있습니다.";
            else if (Phase != LobbyPhase.HostSetup) error = "양쪽 준비 완료 후 한 번만 제출할 수 있습니다.";
            else if (string.IsNullOrWhiteSpace(topic)) error = "토론할 주제를 입력해 주세요.";
            else if (topic.Length > 1000) error = "주제는 1,000자 이내로 입력해 주세요.";
            else if (!int.TryParse(rounds, out var count) || count < 1 || count > 5)
                error = "최대 라운드는 1~5 사이의 정수로 입력해 주세요.";
            if (error.Length != 0) return false;
            Topic = topic.Trim();
            MaxRound = int.Parse(rounds);
            Phase = LobbyPhase.Submitted;
            Revision++;
            Changed?.Invoke();
            return true;
        }

        private static bool ValidRole(PlayerRole role) => role == PlayerRole.Host || role == PlayerRole.Guest;

        public LobbySnapshot Snapshot() => new LobbySnapshot {
            sessionId=SessionId, revision=Revision, phase=Phase,
            hostReady=HostReady, guestReady=GuestReady, topic=Topic, maxRound=MaxRound,mapId=MapId
        };

        public bool ApplySnapshot(LobbySnapshot data, bool firstSnapshot = false)
        {
            if (data == null || string.IsNullOrEmpty(data.sessionId) || data.revision < 0 ||
                !Enum.IsDefined(typeof(LobbyPhase),data.phase) || data.maxRound < 1 || data.maxRound > 5 ||
                data.topic == null || data.topic.Length > 1000) return false;
            if(data.mapId!="" && data.mapId!="court")return false;
            if((data.hostReady || data.guestReady || data.phase!=LobbyPhase.Preparing) && data.mapId!="court")return false;
            if (!firstSnapshot && (data.sessionId != SessionId || data.revision <= Revision)) return false;
            if (data.phase != LobbyPhase.Preparing && (!data.hostReady || !data.guestReady)) return false;
            if (data.phase == LobbyPhase.Submitted && string.IsNullOrWhiteSpace(data.topic)) return false;
            SessionId=data.sessionId; Revision=data.revision; Phase=data.phase;
            HostReady=data.hostReady; GuestReady=data.guestReady; Topic=data.topic; MaxRound=data.maxRound;
            MapId=data.mapId;
            Changed?.Invoke();
            return true;
        }
    }
}
