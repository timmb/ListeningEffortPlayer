using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Video;

using PupilometryData = Tobii.XR.TobiiXR_AdvancedEyeTrackingData;


public class ScriptedSessionController : MonoBehaviour
{
    public Session session { get; private set; }

    public VideoCatalogue videoCatalogue;
    public AudioRecorder audioRecorder;
    public Pupilometry pupilometry;
    public TransformWatcher headTransform;
    public ColorCalibrationSphere brightnessCalibrationSphere;

    public VideoPlayer skyboxVideoPlayer;
    public VideoManager[] videoManagers;
    public GameObject[] babblePrefabs;

    public event EventHandler<State> stateChanged;
    /// current number (0 indexed), current label (1 indexed), total number
    public event EventHandler<(int current, string currentLabel, int total)> challengeNumberChanged;

    struct SessionEventLogEntry
    {
        public string Timestamp { get; set; }
        public string SessionTime { get; set; }
        public string Configuration { get; set; }
        public string EventName { get; set; }
        public string ChallengeNumber { get; set; }
        public string LeftVideo { get; set; }
        public string MiddleVideo { get; set; }
        public string RightVideo { get; set; }
        public string UserResponseAudioFile { get; set; }

        public string HeadRotationEulerX { get; set; }
        public string HeadRotationEulerY { get; set; }
        public string HeadRotationEulerZ { get; set; }

        public string PupilometrySystemTimestamp { get; set; }
        public string PupilometryDeviceTimestamp { get; set; }
        public string LeftIsBlinking { get; set; }
        public string RightIsBlinking { get; set; }
        public string LeftPupilDiameterValid { get; set; }
        public string LeftPupilDiameter { get; set; }
        public string RightPupilDiameterValid { get; set; }
        public string RightPupilDiameter { get; set; }
        public string LeftPositionGuideValid { get; set; }
        public string LeftPositionGuideX { get; set; }
        public string LeftPositionGuideY { get; set; }
        public string RightPositionGuideValid { get; set; }
        public string RightPositionGuideX { get; set; }
        public string RightPositionGuideY { get; set; }
        public string LeftGazeRayIsValid { get; set; }
        public string LeftGazeRayOriginX { get; set; }
        public string LeftGazeRayOriginY { get; set; }
        public string LeftGazeRayOriginZ { get; set; }
        public string LeftGazeRayDirectionX { get; set; }
        public string LeftGazeRayDirectionY { get; set; }
        public string LeftGazeRayDirectionZ { get; set; }
        public string RightGazeRayIsValid { get; set; }
        public string RightGazeRayOriginX { get; set; }
        public string RightGazeRayOriginY { get; set; }
        public string RightGazeRayOriginZ { get; set; }
        public string RightGazeRayDirectionX { get; set; }
        public string RightGazeRayDirectionY { get; set; }
        public string RightGazeRayDirectionZ { get; set; }
        public string ConvergenceDistanceIsValid { get; set; }
        public string ConvergenceDistance { get; set; }
        public string GazeRayIsValid { get; set; }
        public string GazeRayOriginX { get; set; }
        public string GazeRayOriginY { get; set; }
        public string GazeRayOriginZ { get; set; }
        public string GazeRayDirectionX { get; set; }
        public string GazeRayDirectionY { get; set; }
        public string GazeRayDirectionZ { get; set; }

    }
    public enum State
    {
        Inactive,
        LoadingSession,
        WaitingForUserToStartBrightnessCalibration,
        PerformingBrightnessCalibration,
        WaitingForUserToStartChallenges,
        UserReadyToStartChallenges,
        DelayingBeforePlayingVideo,
        PlayingVideo,
        DelayingAfterPlayingVideos,
        RecordingUserResponse,
        AudioRecordingComplete,
        Completed,
    }
    private State _state = State.Inactive;
    public State state
    {
        get => _state; private set
        {
            Debug.Log($"Changing state from {_state} to {value}");
            Debug.Assert(AllowedTransitions[_state].Contains(value));
            _state = value;
            stateChanged?.Invoke(this, _state);
        }
    }

    static readonly Dictionary<State, State[]> AllowedTransitions = new Dictionary<State, State[]>
        {
            {State.Inactive, new State[]{ State.LoadingSession } },
            {State.LoadingSession, new State[]{ State.WaitingForUserToStartBrightnessCalibration, State.WaitingForUserToStartChallenges, State.Completed } },
            {State.WaitingForUserToStartBrightnessCalibration, new State[]{ State.PerformingBrightnessCalibration } },
            {State.PerformingBrightnessCalibration, new State[]{ State.WaitingForUserToStartChallenges } },
            {State.WaitingForUserToStartChallenges, new State[]{ State.UserReadyToStartChallenges } },
            {State.UserReadyToStartChallenges, new State[]{ State.DelayingBeforePlayingVideo } },
            {State.DelayingBeforePlayingVideo, new State[]{ State.PlayingVideo } },
            {State.PlayingVideo, new State[]{ State.DelayingAfterPlayingVideos } },
            {State.DelayingAfterPlayingVideos, new State[]{ State.RecordingUserResponse } },
            {State.RecordingUserResponse, new State[]{ State.AudioRecordingComplete } },
            {State.AudioRecordingComplete, new State[]{ State.WaitingForUserToStartChallenges, State.Completed } },
            {State.Completed, new State[]{ } },
        };

    private int numVideosPlaying = 0;
    private StreamWriter sessionEventLogWriter;

    void Start()
    {
        for (int i = 0; i < 3; i++)
        {
            videoManagers[i].playbackFinished += (_, _) =>
            {
                if (state != State.Inactive)
                {
                    numVideosPlaying--;
                    Debug.Assert(0 <= numVideosPlaying || numVideosPlaying < 3);
                };
            };
        }
        audioRecorder.recordingFinished += (_, _) =>
        {
            Debug.Assert(state == State.RecordingUserResponse);
            state = State.AudioRecordingComplete;
        };

 
    }

    // yamlPath should be an absolute path including extension
    public void StartSession(string yamlPath)
    {
        session = Session.LoadFromYamlPath(yamlPath, videoCatalogue);
        Debug.Assert(session.VideoScreens.Count() == 3);
        Debug.Assert(videoManagers.Count() == 3);
        Debug.Log($"Loaded {yamlPath}");

        StartCoroutine(SessionCoroutine());
    }


    public void onUserReadyToContinue()
    {
        if (state == State.WaitingForUserToStartBrightnessCalibration)
        {
            state = State.PerformingBrightnessCalibration;
        }
        else if (state == State.WaitingForUserToStartChallenges)
        {
            state = State.UserReadyToStartChallenges;
        }
        else
        {
            Debug.Assert(false);
        }
    }

    public void onUserReadyToStopRecording()
    {
        Debug.Assert(state == State.RecordingUserResponse);
        Debug.Assert(audioRecorder.isRecording);
        audioRecorder.StopRecording();
    }

    private IEnumerator SessionCoroutine()
    {
        Debug.Log($"Starting automated trial session: {session.Name}");
        state = State.LoadingSession;

        DateTime sessionStartTimeUTC = DateTime.UtcNow;
        string localTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string subjectLabel = PlayerPrefs.GetString("subjectLabel", "");
        string sessionLabel = $"{localTimestamp}_{session.Name}{(subjectLabel != "" ? "_" : "")}{subjectLabel}";
        string sessionFolder = Path.Join(Path.Join(Application.persistentDataPath, "RecordedSessions"), sessionLabel);
        audioRecorder.saveDirectory = sessionFolder;
        Directory.CreateDirectory(sessionFolder);
        File.WriteAllText(Path.Join(sessionFolder, session.Name != ""? session.Name+".yaml" : "session.yaml"), session.yaml);

        // Speaker Amplitude
        videoManagers.ToList().ForEach(vm => vm.audioSource.volume = session.SpeakerAmplitude);

        // MaskingVideo
        videoCatalogue.SetPlayerSource(skyboxVideoPlayer, session.MaskingVideo);
        skyboxVideoPlayer.Play();

        // Maskers
        if (session.Maskers.Count() > babblePrefabs.Count())
        {
            throw new System.Exception($"There are {session.Maskers.Count()} maskers defined in YAML but only {babblePrefabs.Count()} babble sources available.");
        }
        for (int i = 0; i < session.Maskers.Count(); i++)
        {
            Debug.Assert(babblePrefabs[i].GetComponentsInChildren<AudioSource>().Count() == 1);
            babblePrefabs[i].GetComponentInChildren<AudioSource>().volume = session.Maskers[i].Amplitude;
            babblePrefabs[i].transform.localRotation = Quaternion.Euler(0, session.Maskers[i].Rotation, 0);
            babblePrefabs[i].GetComponentInChildren<AudioSource>().Play();
            if (!session.PlayMaskersContinuously)
            {
                babblePrefabs[i].GetComponentInChildren<AudioSource>().Pause();
            }
            Debug.Log($"Set masker {i} to {session.Maskers[i].Amplitude} amplitude and {session.Maskers[i].Rotation} rotation.");
        }
        for (int i = session.Maskers.Count(); i < babblePrefabs.Count(); i++)
        {
            babblePrefabs[i].SetActive(false);
            Debug.Log($"Deactivated masker {i} as not set in session YAML.");
        }

        // Setup screens
        for (int i = 0; i < 3; i++)
        {
            videoManagers[i].idleVideoName = session.VideoScreens[i].IdleVideo;
            var s = session.VideoScreens[i];
            videoManagers[i].SetPosition(s.Inclination, s.Azimuth, s.Twist, s.RotationOnXAxis, s.RotationOnYAxis, s.ScaleWidth, s.ScaleHeight);
        }

        // Setup logging
        using var sessionEventLogWriter = new StreamWriter(Path.Join(sessionFolder, $"{sessionLabel}_events.csv"), true, Encoding.UTF8);
        LogUtilities.writeCSVLine(sessionEventLogWriter, new SessionEventLogEntry
        {
            Timestamp = LogUtilities.localTimestamp(),
            SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
            EventName = "Trial started",
            Configuration = session.Name,
        });

        // Perform Brightness Calibration

        if (session.BrightnessCalibrationDurationFromBlackToWhite>0.0001 && session.BrightnessCalibrationDurationToHoldOnWhite > 0.0001)
        {
            state = State.WaitingForUserToStartBrightnessCalibration;
            yield return new WaitUntil(() => state != State.WaitingForUserToStartBrightnessCalibration);
            Debug.Assert(state ==State.PerformingBrightnessCalibration);

            brightnessCalibrationSphere.gameObject.SetActive(true);
            brightnessCalibrationSphere.brightness = 0.0f;
            float startTime = Time.time;
            while (brightnessCalibrationSphere.brightness < 1.0f)
            {
                float t = Time.time - startTime;
                brightnessCalibrationSphere.brightness = Math.Min(1.0f, t / session.BrightnessCalibrationDurationFromBlackToWhite);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(session.BrightnessCalibrationDurationToHoldOnWhite);
            brightnessCalibrationSphere.gameObject.SetActive(false);
        }

        // Wait for user

        state = State.WaitingForUserToStartChallenges;
        yield return new WaitUntil(() => state != State.WaitingForUserToStartChallenges);

        // Start challenges

        Debug.Assert(state == State.UserReadyToStartChallenges);
        for (int i = 0; i < 3; i++)
        {
            videoManagers[i].StartIdleVideo();
        }

        for (int i = 0; i < session.Challenges.Count(); i++)
        {
            state = State.DelayingBeforePlayingVideo;

            // Prepare challenge

            if (!session.PlayMaskersContinuously)
            {
                foreach (GameObject babblePrefab in babblePrefabs)
                {
                    babblePrefab.GetComponentInChildren<AudioSource>().UnPause();
                }
            }

            string challengeLabel = (i + 1).ToString();
            challengeNumberChanged?.Invoke(this, (current: i, currentLabel: challengeLabel, total: session.Challenges.Count()));
            string challengeLabelPadded = $"{i+1:000}";
            string userResponseAudioFile = $"{sessionLabel}_response_{challengeLabelPadded:000}.wav";

            // Record a separate CSV for pupilometry and head rotation for each challenge
            using var pupilometryLogWriter = new StreamWriter(Path.Join(sessionFolder, $"{sessionLabel}_pupilometry_{challengeLabelPadded}.csv"), true, Encoding.UTF8);
            EventHandler<PupilometryData> pupilometryCallback = createPupilometryCallback(pupilometryLogWriter, sessionStartTimeUTC, challengeLabel);
            pupilometry.DataChanged += pupilometryCallback;
            EventHandler<Transform> headTransformCallback = createHeadTransformCallback(pupilometryLogWriter, sessionStartTimeUTC, challengeLabel);
            headTransform.TransformChanged += headTransformCallback;

            yield return new WaitForSeconds(session.DelayBeforePlayingVideos);
            state = State.PlayingVideo;
            Debug.Assert(numVideosPlaying == 0);

            // Play Videos

            LogUtilities.writeCSVLine(sessionEventLogWriter, new SessionEventLogEntry
            {
                Timestamp = LogUtilities.localTimestamp(),
                SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
                Configuration = session.Name,
                EventName = "Playing videos",
                ChallengeNumber = challengeLabel,
                LeftVideo = session.Challenges[i][0],
                MiddleVideo = session.Challenges[i][1],
                RightVideo = session.Challenges[i][2],
            });
            for (int k = 0; k < 3; k++)
            {
                numVideosPlaying++;
                videoManagers[k].PlayVideo(session.Challenges[i][k]);
            }
            // start recording now because pico loses the first few seconds
            double expectedPlaybackDuration = videoManagers.Aggregate(0.0, (acc, vm) => Math.Max(acc, vm.player.length));
            int maximumRecordingDuration = session.RecordingDuration + (int)Math.Ceiling(expectedPlaybackDuration);
            Debug.Log($"Starting off recorder. Max duration (including playback): {maximumRecordingDuration}");
            audioRecorder.StartRecording(userResponseAudioFile, maximumRecordingDuration);

            while (numVideosPlaying > 0)
            {
                yield return null;
            }
            state = State.DelayingAfterPlayingVideos;
            yield return new WaitForSeconds(session.DelayAfterPlayingVideos);
            if (!session.PlayMaskersContinuously)
            {
                foreach (GameObject babblePrefab in babblePrefabs)
                {
                    babblePrefab.GetComponentInChildren<AudioSource>().Pause();
                }
            }

            // Record User

            state = State.RecordingUserResponse;
            audioRecorder.MarkRecordingInPoint();
            LogUtilities.writeCSVLine(sessionEventLogWriter, new SessionEventLogEntry
            {
                Timestamp = LogUtilities.localTimestamp(),
                SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
                EventName = "Recording response",
                Configuration = session.Name,
                ChallengeNumber = challengeLabel,
                LeftVideo = session.Challenges[i][0],
                MiddleVideo = session.Challenges[i][1],
                RightVideo = session.Challenges[i][2],
            });

            yield return new WaitUntil(() => state != State.RecordingUserResponse);

            Debug.Assert(state == State.AudioRecordingComplete);

            LogUtilities.writeCSVLine(sessionEventLogWriter, new SessionEventLogEntry
            {
                Timestamp = LogUtilities.localTimestamp(),
                SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
                EventName = "Response received",
                Configuration = session.Name,
                ChallengeNumber = challengeLabel,
                LeftVideo = session.Challenges[i][0],
                MiddleVideo = session.Challenges[i][1],
                RightVideo = session.Challenges[i][2],
                UserResponseAudioFile = userResponseAudioFile,
            });
            pupilometry.DataChanged -= pupilometryCallback;
            headTransform.TransformChanged -= headTransformCallback;
        }

        state = State.Completed;


        LogUtilities.writeCSVLine(sessionEventLogWriter, new SessionEventLogEntry
        {
            Timestamp = LogUtilities.localTimestamp(),
            SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
            EventName = "Trial completed",
            Configuration = session.Name,
        });

        sessionEventLogWriter.Close();
    }


    private EventHandler<PupilometryData> createPupilometryCallback(StreamWriter logWriter, DateTime sessionStartTimeUTC, string challengeNumber)
    {
        EventHandler<PupilometryData> pupilometryCallback = (object sender, PupilometryData data) =>
        {
            LogUtilities.writeCSVLine(logWriter, new SessionEventLogEntry
            {
                Timestamp = LogUtilities.localTimestamp(),
                SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
                Configuration = session.Name,
                EventName = "Pupilometry",
                ChallengeNumber = challengeNumber,
                PupilometrySystemTimestamp = data.SystemTimestamp.ToString(),
                PupilometryDeviceTimestamp = data.DeviceTimestamp.ToString(),
                LeftIsBlinking = data.Left.IsBlinking.ToString(),
                RightIsBlinking = data.Right.IsBlinking.ToString(),
                LeftPupilDiameterValid = data.Left.PupilDiameterValid.ToString(),
                LeftPupilDiameter = data.Left.PupilDiameter.ToString(),
                RightPupilDiameterValid = data.Right.PupilDiameterValid.ToString(),
                RightPupilDiameter = data.Right.PupilDiameter.ToString(),
                LeftPositionGuideValid = data.Left.PositionGuideValid.ToString(),
                LeftPositionGuideX = data.Left.PositionGuide.x.ToString(),
                LeftPositionGuideY = data.Left.PositionGuide.y.ToString(),
                RightPositionGuideValid = data.Right.PositionGuideValid.ToString(),
                RightPositionGuideX = data.Right.PositionGuide.x.ToString(),
                RightPositionGuideY = data.Right.PositionGuide.y.ToString(),
                LeftGazeRayIsValid = data.Left.GazeRay.IsValid.ToString(),
                LeftGazeRayOriginX = data.Left.GazeRay.Origin.x.ToString(),
                LeftGazeRayOriginY = data.Left.GazeRay.Origin.y.ToString(),
                LeftGazeRayOriginZ = data.Left.GazeRay.Origin.z.ToString(),
                LeftGazeRayDirectionX = data.Left.GazeRay.Direction.x.ToString(),
                LeftGazeRayDirectionY = data.Left.GazeRay.Direction.y.ToString(),
                LeftGazeRayDirectionZ = data.Left.GazeRay.Direction.z.ToString(),
                RightGazeRayIsValid = data.Right.GazeRay.IsValid.ToString(),
                RightGazeRayOriginX = data.Right.GazeRay.Origin.x.ToString(),
                RightGazeRayOriginY = data.Right.GazeRay.Origin.y.ToString(),
                RightGazeRayOriginZ = data.Right.GazeRay.Origin.z.ToString(),
                RightGazeRayDirectionX = data.Right.GazeRay.Direction.x.ToString(),
                RightGazeRayDirectionY = data.Right.GazeRay.Direction.y.ToString(),
                RightGazeRayDirectionZ = data.Right.GazeRay.Direction.z.ToString(),
                ConvergenceDistanceIsValid = data.ConvergenceDistanceIsValid.ToString(),
                ConvergenceDistance = data.ConvergenceDistance.ToString(),
                GazeRayIsValid = data.GazeRay.IsValid.ToString(),
                GazeRayOriginX = data.GazeRay.Origin.x.ToString(),
                GazeRayOriginY = data.GazeRay.Origin.y.ToString(),
                GazeRayOriginZ = data.GazeRay.Origin.z.ToString(),
                GazeRayDirectionX = data.GazeRay.Direction.x.ToString(),
                GazeRayDirectionY = data.GazeRay.Direction.y.ToString(),
                GazeRayDirectionZ = data.GazeRay.Direction.z.ToString(),
            });
        };
        return pupilometryCallback;
    }

    private EventHandler<Transform> createHeadTransformCallback(StreamWriter logWriter, DateTime sessionStartTimeUTC, string challengeNumber)
    {
        EventHandler<Transform> headTransformCallback = (object sender, Transform data) =>
        {
            LogUtilities.writeCSVLine(logWriter, new SessionEventLogEntry
            {
                Timestamp = LogUtilities.localTimestamp(),
                SessionTime = (DateTime.UtcNow - sessionStartTimeUTC).TotalSeconds.ToString("F3"),
                Configuration = session.Name,
                EventName = "HeadRotation",
                ChallengeNumber = challengeNumber,
                HeadRotationEulerX = data.rotation.eulerAngles.x.ToString(),
                HeadRotationEulerY = data.rotation.eulerAngles.y.ToString(),
                HeadRotationEulerZ = data.rotation.eulerAngles.z.ToString(),
            });
        };
        return headTransformCallback;
    }
}
