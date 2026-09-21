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
        public string hostKeyword,guestKeyword,caseSummary,commonQuestion,aiError;
        public bool aiGenerating;
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
        public string HostKeyword { get; private set; }="";
        public string GuestKeyword { get; private set; }="";
        public string CaseSummary { get; private set; }="";
        public string CommonQuestion { get; private set; }="";
        public string AiError { get; private set; }="";
        public bool AiGenerating { get; private set; }
        public bool SetKeyword(PlayerRole role,string value)
        {
            if(Phase!=LobbyPhase.HostSetup || !ValidRole(role) || AiGenerating || CaseSummary.Length>0 || string.IsNullOrWhiteSpace(value) || value.Trim().Length>40)return false;
            if(role==PlayerRole.Host)HostKeyword=value.Trim();else GuestKeyword=value.Trim();
            AiError="";Revision++;Changed?.Invoke();return true;
        }
        public bool BeginCaseGeneration(PlayerRole role)
        {
            if(role!=PlayerRole.Host || Phase!=LobbyPhase.HostSetup || AiGenerating || CaseSummary.Length>0 || HostKeyword.Length==0 || GuestKeyword.Length==0)return false;
            AiGenerating=true;AiError="";Revision++;Changed?.Invoke();return true;
        }
        public bool CompleteCaseGeneration(PlayerRole role,string summary,string question,string error="")
        {
            if(role!=PlayerRole.Host || Phase!=LobbyPhase.HostSetup || !AiGenerating)return false;
            bool valid=string.IsNullOrEmpty(error) && !string.IsNullOrWhiteSpace(summary) && summary.Length<=300 && !string.IsNullOrWhiteSpace(question) && question.Length<=150;
            AiGenerating=false;
            if(valid){CaseSummary=summary.Trim();CommonQuestion=question.Trim();AiError="";}
            else AiError=string.IsNullOrWhiteSpace(error)?"AI 응답 형식이 올바르지 않습니다.":error.Substring(0,Math.Min(error.Length,200));
            Revision++;Changed?.Invoke();return valid;
        }
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
            else if(AiGenerating)error="AI 사건 생성이 끝날 때까지 기다려 주세요.";
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
            hostReady=HostReady, guestReady=GuestReady, topic=Topic, maxRound=MaxRound,mapId=MapId,
            hostKeyword=HostKeyword,guestKeyword=GuestKeyword,caseSummary=CaseSummary,commonQuestion=CommonQuestion,aiGenerating=AiGenerating,aiError=AiError
        };

        public bool ApplySnapshot(LobbySnapshot data, bool firstSnapshot = false)
        {
            if (data == null || string.IsNullOrEmpty(data.sessionId) || data.revision < 0 ||
                !Enum.IsDefined(typeof(LobbyPhase),data.phase) || data.maxRound < 1 || data.maxRound > 5 ||
                data.topic == null || data.topic.Length > 1000) return false;
            if(data.mapId!="" && data.mapId!="court")return false;
            if(data.hostKeyword==null || data.hostKeyword.Length>40 || data.guestKeyword==null || data.guestKeyword.Length>40 || data.caseSummary==null || data.caseSummary.Length>300 || data.commonQuestion==null || data.commonQuestion.Length>150 || data.aiError==null || data.aiError.Length>200)return false;
            if((data.caseSummary.Length==0)!=(data.commonQuestion.Length==0))return false;
            if(data.aiGenerating && (data.phase!=LobbyPhase.HostSetup || data.caseSummary.Length>0 || string.IsNullOrWhiteSpace(data.hostKeyword) || string.IsNullOrWhiteSpace(data.guestKeyword)))return false;
            if((data.hostReady || data.guestReady || data.phase!=LobbyPhase.Preparing) && data.mapId!="court")return false;
            if (!firstSnapshot && (data.sessionId != SessionId || data.revision <= Revision)) return false;
            if (data.phase != LobbyPhase.Preparing && (!data.hostReady || !data.guestReady)) return false;
            if (data.phase == LobbyPhase.Submitted && string.IsNullOrWhiteSpace(data.topic)) return false;
            SessionId=data.sessionId; Revision=data.revision; Phase=data.phase;
            HostReady=data.hostReady; GuestReady=data.guestReady; Topic=data.topic; MaxRound=data.maxRound;
            MapId=data.mapId;
            HostKeyword=data.hostKeyword;GuestKeyword=data.guestKeyword;CaseSummary=data.caseSummary;CommonQuestion=data.commonQuestion;AiGenerating=data.aiGenerating;AiError=data.aiError;
            Changed?.Invoke();
            return true;
        }
    }
}
