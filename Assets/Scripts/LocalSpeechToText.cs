using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Justitia
{
    public sealed class LocalSpeechToText : MonoBehaviour
    {
        public string Status { get; private set; }="한국어 STT 대기";
        public string LastTranscript { get; private set; }="";
        public bool Busy => job!=null;
        public event Action<string> Transcribed;
        private SteamVoiceChat voice;
        private MemoryStream utterance;
        private Task<string> job;
        private CancellationTokenSource cancellation;
        private string root,temporary;
        private int generation,jobGeneration;
        public static string RuntimeRoot => Path.Combine(Path.GetDirectoryName(Application.dataPath),"Tools","Whisper");
        public static bool SelfTestRequested => Array.IndexOf(Environment.GetCommandLineArgs(),"--stt-self-test")>=0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static async void RunBuildSelfTest()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--stt-self-test");
            if(index<0)return;
            int exit=1;
            try
            {
                if(index+1>=args.Length)throw new ArgumentException("Expected synthetic WAV path");
                byte[] pcm=null;
                using(var reader=new BinaryReader(File.OpenRead(args[index+1])))
                {
                    reader.ReadBytes(12);
                    while(reader.BaseStream.Position+8<=reader.BaseStream.Length)
                    {
                        string id=Encoding.ASCII.GetString(reader.ReadBytes(4));int size=reader.ReadInt32();
                        if(size<0 || size>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException();
                        if(id=="data"){pcm=reader.ReadBytes(size);break;}
                        reader.BaseStream.Position+=size+(size%2);
                    }
                }
                if(pcm==null)throw new InvalidDataException("No PCM data");
                string runtime=RuntimeRoot,temp=Application.temporaryCachePath;
                Debug.Log("[STT self-test] Runtime: "+runtime);
                string result=await Task.Run(()=>TranscribePcm(runtime,temp,pcm,CancellationToken.None));
                Debug.Log("[STT self-test] Result: "+result.Trim());
                exit=result.Contains("한국어")?0:1;
            }
            catch(Exception error){Debug.LogError("[STT self-test] "+error.Message);}
            Application.Quit(exit);
        }
#endif
        public void Initialize(SteamVoiceChat source)
        {
            voice=source;
            // Windows Player dataPath is <exe directory>/<game>_Data; Mono's BaseDirectory may point inside it.
            root=RuntimeRoot;
            temporary=Application.temporaryCachePath;
            voice.CaptureStarted+=Begin;voice.LocalPcm+=Append;voice.CaptureEnded+=End;
        }
        private void Begin()
        {
            utterance?.Dispose();utterance=null;
            if(Busy){Status="이전 발언 인식 중 · 이번 발언은 STT 생략";return;}
            if(!File.Exists(Path.Combine(root,"Release","whisper-cli.exe")) || !File.Exists(Path.Combine(root,"ggml-small-q5_1.bin")))
            {Status="STT 실행 파일 또는 모델이 없습니다. Tools/Whisper를 확인하세요.";Debug.LogWarning(Status+" 확인 경로: "+root);return;}
            utterance=new MemoryStream();Status="한국어 듣는 중…";
        }
        private void Append(byte[] pcm,int length)
        {
            if(utterance==null)return;
            int remaining=16000*2*32-(int)utterance.Length;
            if(remaining>0)utterance.Write(pcm,0,Math.Min(length,remaining));
        }
        private void End(bool cancelled)
        {
            if(utterance==null)return;
            var pcm=utterance.ToArray();utterance.Dispose();utterance=null;
            if(cancelled){Status="발언 취소";return;}
            double energy=0;
            for(int i=0;i+1<pcm.Length;i+=2){double sample=(short)(pcm[i]|pcm[i+1]<<8)/32768.0;energy+=sample*sample;}
            if(pcm.Length<16000 || Math.Sqrt(energy/Math.Max(1,pcm.Length/2))<0.003)
            {Status="인식할 음성이 없습니다. V 키를 누르고 또렷하게 말해 주세요.";return;}
            Status="한국어 인식 중…";cancellation=new CancellationTokenSource();jobGeneration=generation;
            string runtime=root,temp=temporary;var token=cancellation.Token;
            job=Task.Run(()=>TranscribePcm(runtime,temp,pcm,token));
        }
        private void Update()
        {
            if(job==null || !job.IsCompleted)return;
            var finished=job;job=null;cancellation.Dispose();cancellation=null;
            if(jobGeneration!=generation)return;
            if(finished.IsCanceled){Status="STT 취소";return;}
            if(finished.IsFaulted){Status="STT 실패: "+finished.Exception.GetBaseException().Message;Debug.LogWarning(Status);return;}
            LastTranscript=finished.Result.Trim();
            if(string.IsNullOrEmpty(LastTranscript)){Status="인식된 말이 없습니다.";return;}
            Status="한국어 STT 완료";
            Debug.Log("[STT/ko] "+LastTranscript);
            Transcribed?.Invoke(LastTranscript);
        }
        public void Cancel()
        {generation++;cancellation?.Cancel();utterance?.Dispose();utterance=null;Status="한국어 STT 대기";LastTranscript="";}

        // Runs only on a worker thread; WAV and output files are unique and removed after each request.
        public static string TranscribePcm(string runtime,string temp,byte[] pcm,CancellationToken token)
        {
            string prefix=Path.Combine(temp,"justitia-stt-"+Guid.NewGuid().ToString("N"));
            string wav=prefix+".wav",output=prefix+".txt";
            try
            {
                token.ThrowIfCancellationRequested();Directory.CreateDirectory(temp);
                using(var file=File.Create(wav))using(var writer=new BinaryWriter(file))
                {
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+pcm.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(pcm.Length);writer.Write(pcm);
                }
                var info=new System.Diagnostics.ProcessStartInfo
                {
                    FileName=Path.Combine(runtime,"Release","whisper-cli.exe"),
                    Arguments="-m \""+Path.Combine(runtime,"ggml-small-q5_1.bin")+"\" -f \""+wav+"\" -l ko -otxt -of \""+prefix+"\" -nt -np -t 4 -bs 1 -bo 1",
                    UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,
                    RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                };
                using(var process=System.Diagnostics.Process.Start(info))
                {
                    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    while(!process.WaitForExit(100))
                    {
                        if(token.IsCancellationRequested || timer.Elapsed.TotalSeconds>120)
                        {try{process.Kill();process.WaitForExit(3000);}catch(InvalidOperationException){} token.ThrowIfCancellationRequested();throw new TimeoutException("인식 시간이 120초를 초과했습니다.");}
                    }
                    token.ThrowIfCancellationRequested();
                    if(process.ExitCode!=0 || !File.Exists(output))throw new InvalidOperationException("Whisper 실행 실패 (코드 "+process.ExitCode+"). 모델과 실행 파일을 확인하세요.");
                    // Observe both read tasks after exit to ensure redirected pipes have drained.
                    stdout.GetAwaiter().GetResult();stderr.GetAwaiter().GetResult();
                    return File.ReadAllText(output,Encoding.UTF8);
                }
            }
            finally
            {
                if(File.Exists(wav))File.Delete(wav);
                if(File.Exists(output))File.Delete(output);
            }
        }
        private void OnDisable()=>Cancel();
        private void OnDestroy()
        {Cancel();if(voice){voice.CaptureStarted-=Begin;voice.LocalPcm-=Append;voice.CaptureEnded-=End;}}
    }
}
