using Google.Api.Gax.Grpc;
using Google.Cloud.Speech.V1;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.Logging;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using log4net;
using log4net.Config;
using WebRtcVadSharp;
using System;
using static Google.Cloud.Speech.V1.StreamingRecognitionConfig.Types;

namespace SIPClient2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    class MicAudio 
    {
        private WaveInEvent waveIn;
        protected CancellationTokenSource _cancellationTokenSource;

        public MicAudio(BlockingCollection<byte[]> AudioBuff)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(8000, 1) // Sample rate and channels
            };

            waveIn.DataAvailable += (sender, e) =>
            {
                var samples = e.Buffer.Clone() as byte[];

                AudioBuff.Add(samples);
            };
        }

        public void Start()
        {
            Task.Run(() =>
            {
                waveIn.StartRecording();

                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    System.Threading.Thread.Sleep(100);
                }

                waveIn.StopRecording();
            });

        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

    }


    class FileReader
    {
        private readonly BlockingCollection<byte[]> audioBuff;
        public List<bool> speechFlags;
        private readonly ILog log = LogManager.GetLogger(typeof(STT));

        public FileReader(BlockingCollection<byte[]> AudioBuff)
        {
            audioBuff = AudioBuff;
            speechFlags = new List<bool>();

        }
        private bool HasSpeech(byte[] audioFrame)
        {
            int MS20 = 160;
            int NoIter = audioFrame.Length / MS20;

            bool isSpeech = false;


            using (var vad = new WebRtcVad())
            {
                vad.OperatingMode = OperatingMode.HighQuality;

                for (int i = 0; i < NoIter; i++)
                {
                    var bty = Google.Protobuf.ByteString.CopyFrom(audioFrame, i * MS20, MS20).ToByteArray();
                    isSpeech = vad.HasSpeech(bty, SampleRate.Is8kHz, FrameLength.Is20ms);

                    speechFlags.Add(isSpeech);
                }
            }


            return isSpeech;
        }
        public void Start(string AudioFilePath, SpeechClient.StreamingRecognizeStream StreamingCall)
        {
            Task.Run(async () =>
            {
                using (var audioStream = File.OpenRead(AudioFilePath))
                {
                    var buffer = new byte[1600];
                    int bytesRead;

                    while ((bytesRead = audioStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        //HasSpeech(buffer);

                        await StreamingCall.WriteAsync(new StreamingRecognizeRequest
                        {
                            AudioContent = Google.Protobuf.ByteString.CopyFrom(buffer)
                        });

                    }
                }

                await StreamingCall.WriteCompleteAsync();

                string result = string.Concat(speechFlags.Select(b => b ? '1' : '0'));

                //log.Info(result);
            });

        }
                    
    }
    class STT
    {
        private readonly BlockingCollection<byte[]> audioBuff;
        public SpeechClient.StreamingRecognizeStream StreamingCall;
        private readonly ILog log = LogManager.GetLogger(typeof(STT));
        public List<bool> speechFlags;

        public STT(BlockingCollection<byte[]> AudioBuff)
        {
            audioBuff = AudioBuff;
            speechFlags = new List<bool>();

        }

        public async Task StartStream()
        {
            string jsonCredentialsPath = "alkhwarizmispeaker-6ccdee4e784a.json";

            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", jsonCredentialsPath);

            var client = SpeechClient.Create();
            StreamingCall = client.StreamingRecognize();
            var responseStream = StreamingCall.GetResponseStream();
            var streamingConfig = new StreamingRecognitionConfig
            {
                Config = new RecognitionConfig
                {
                    Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                    SampleRateHertz = 8000,
                    LanguageCode = LanguageCodes.Arabic.SaudiArabia,
                },
                InterimResults = false,
                //VoiceActivityTimeout=VoiceActivityTimeout.
            };

            await StreamingCall.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = streamingConfig
            });

            _ = Transcript(responseStream);
        }


        private async Task Transcript(AsyncResponseStream<StreamingRecognizeResponse> responseStream)
        {
            string text;

            try
            {
                await foreach (var response in responseStream)
                {
                    foreach (var result in response.Results)
                    {
                        if (!result.IsFinal)
                            continue;

                        text = result.Alternatives[0].Transcript;


                        log.Info($"Transcript: {text}");

                    }
                }
            }

            catch (Exception ex)
            {
                // when long duratuion elapsed without audio
                log.Error($"Exception: {ex.ToString()}");
            }
        }

        private bool HasSpeech(byte[] audioFrame)
        {
            int MS20 = 160;
            int NoIter = audioFrame.Length / MS20;

            bool isSpeech = false;


            using (var vad = new WebRtcVad())
            {
                for (int i = 0; i < NoIter; i++)
                {

                    isSpeech = vad.HasSpeech(Google.Protobuf.ByteString.CopyFrom(audioFrame, i*MS20, MS20).ToByteArray(), SampleRate.Is8kHz, FrameLength.Is20ms);

                    speechFlags.Add(isSpeech);
                }
            }


            return isSpeech;
        }

        public async Task STTStream()
        {
            await StartStream();

            foreach (var buffer in audioBuff.GetConsumingEnumerable())
            {
                try
                {
                    if (!HasSpeech(buffer))
                        log.Warn("##################################### Speech Signal #####################################");

                    if (buffer.Length > 0)
                    {
                        _ = StreamingCall.WriteAsync(new StreamingRecognizeRequest
                        {
                            AudioContent = Google.Protobuf.ByteString.CopyFrom(buffer)
                        });
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Exception: {ex.ToString()}");
                }
            }

            log.Info(speechFlags.ToString());
        }

    }

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(async () =>
            {
                BlockingCollection<byte[]> audioBuff = new BlockingCollection<byte[]>();

                MicAudio Mic = new MicAudio(audioBuff);
                FileReader File = new FileReader(audioBuff);
                STT Stt = new STT(audioBuff);

                //Mic.Start();
                //await Stt.STTStream();

                await Stt.StartStream();
                File.Start("C:\\src\\SIPService\\SipServerService\\bin\\Debug\\net6.0\\SIPService\\Audio\\c057d083-f7ac-47d4-8ef1-f3ddbd1712b5.wav", Stt.StreamingCall);
                //File.Start("G:\\src\\SIP\\SIPClient2\\SIPClient2\\ad076212-d368-422c-88aa-223781e54acd.wav", Stt.StreamingCall);

                //Mic.Stop();
            });

        }
    }
}