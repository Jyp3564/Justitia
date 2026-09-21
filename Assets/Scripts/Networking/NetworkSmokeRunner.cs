using System;
using System.IO;
using UnityEngine;

namespace Justitia
{
    // Explicit command-line test harness; inactive in normal launches.
    public sealed class NetworkSmokeRunner : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {public bool passed;public string role;public string detail;public long revision;public int topicLength;}
        private LanLobbyConnection connection;
        private string role,output;
        private float deadline,finishAt;
        private bool readySent,submitted,finished,keywordSent;
        private static string TestTopic => "약속 시간 변경에 관한 토론: "+new string('가',960);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--network-smoke")<0)return;
            new GameObject("NetworkSmokeRunner").AddComponent<NetworkSmokeRunner>();
        }

        private void Start()
        {
            var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"--network-smoke");
            role=args[index+1];output=args[index+2];
            ushort port=index+3<args.Length && ushort.TryParse(args[index+3],out var parsed)?parsed:(ushort)17777;
            connection=gameObject.AddComponent<LanLobbyConnection>();deadline=Time.realtimeSinceStartup+45;
            bool started=role=="host"?connection.Host(port):connection.Join("127.0.0.1",port);
            if(!started)Finish(false,"Could not start transport: "+connection.Message);
            else if(role=="host")connection.SelectMap("court");
        }

        private void Update()
        {
            if(finished){if(Time.realtimeSinceStartup>=finishAt)Application.Quit();return;}
            if(!connection)return;
            if(Time.realtimeSinceStartup>deadline){Finish(false,"Timed out: "+connection.Message);return;}
            if(role=="reject")
            {
                if(connection.Stage==ConnectionStage.Offline)Finish(true,"Third client rejected");
                return;
            }
            if(connection.CanReady && connection.Session.MapId=="court" && !readySent)
            {
                connection.SetReady(true);readySent=true;
                if(role!="host" && connection.Submit("Guest spoof","3",out _))
                {Finish(false,"Guest could submit Host configuration");return;}
            }
            if(role=="host")
            {
                if(connection.Session.Phase==LobbyPhase.HostSetup && !submitted)
                {
                    if(!keywordSent){connection.SetKeyword("아이콘");keywordSent=true;}
                    if(connection.Session.GuestKeyword!="리모콘")return;
                    if(!connection.BeginCaseGeneration()){Finish(false,"Could not start AI fixture generation");return;}
                    if(!connection.CompleteCaseGeneration(connection.Session.SessionId,"AI 동기화 검증용 사건입니다.","그때 무엇을 하고 있었습니까?")){Finish(false,"Could not publish AI fixture");return;}
                    if(!connection.Submit(TestTopic,"5",out var error)){Finish(false,error);return;}
                    submitted=true;
                }
                if(submitted && connection.Stage==ConnectionStage.Offline)
                    Finish(true,"Host authoritative ready/configuration flow and Guest disconnect passed");
            }
            else if(connection.Session.Phase==LobbyPhase.HostSetup && !keywordSent)
            {
                connection.SetKeyword("리모콘");keywordSent=true;
                if(connection.BeginCaseGeneration()){Finish(false,"Guest could generate AI case");return;}
            }
            else if(connection.Session.Phase==LobbyPhase.Submitted)
            {
                bool valid=connection.LocalRole==PlayerRole.Guest && connection.Session.HostReady && connection.Session.GuestReady &&
                    connection.Session.Topic==TestTopic && connection.Session.MaxRound==5 && connection.Session.Revision>=8 && connection.Session.MapId=="court" && connection.Session.HostKeyword=="아이콘" && connection.Session.GuestKeyword=="리모콘" && connection.Session.CaseSummary=="AI 동기화 검증용 사건입니다." && connection.Session.CommonQuestion=="그때 무엇을 하고 있었습니까?";
                Finish(valid,"Guest keyword authority and synchronized AI fixture, map, long topic and readiness passed");
                connection.Leave();
            }
        }

        private void Finish(bool passed,string detail)
        {
            if(finished)return;finished=true;finishAt=Time.realtimeSinceStartup+1;
            var report=new Report {passed=passed,role=role,detail=detail,
                revision=connection?connection.Session.Revision:0,topicLength=connection?connection.Session.Topic.Length:0};
            File.WriteAllText(output,JsonUtility.ToJson(report,true));
        }
    }
}
