using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using ARPIANO.Scripts.Lessons;
using ARPIANO.Scripts.MIDI;
using ARPIANO.Scripts.Models;

namespace ARPIANO.Scripts.UI
{
    public class LessonSelectionUI : MonoBehaviour
    {
        private const string LessonsFolderName = "Lessons";

        [Header("Lesson UI")]
        [SerializeField] private TMP_Dropdown lessonDropdown;
        [SerializeField] private Button startLessonButton;
        [SerializeField] private TMP_Text statusText;

        [Header("Playback")]
        [SerializeField] private LessonPlayer lessonPlayer;

        private sealed class LessonEntry
        {
            public string DisplayName;
            public string FileName;
            public string Path;
        }

        private readonly List<LessonEntry> lessonEntries = new();
        private readonly List<string> dropdownOptions = new();

        private bool isLoading;

        private void OnEnable()
        {
            if (!ValidateReferences())
                return;

            startLessonButton.onClick.RemoveListener(StartLesson);
            startLessonButton.onClick.AddListener(StartLesson);

            ConfigureDropdownAppearance();

            RefreshLessons();
        }

        private bool ValidateReferences()
        {
            bool valid = true;

            if (lessonDropdown == null)
            {
                Debug.LogError(
                    "LessonSelectionUI: LessonDropdown is not assigned " +
                    "in the Inspector.");
                valid = false;
            }

            if (startLessonButton == null)
            {
                Debug.LogError(
                    "LessonSelectionUI: StartLessonButton is not assigned " +
                    "in the Inspector.");
                valid = false;
            }

            if (!TryResolveLessonPlayer())
                valid = false;

            return valid;
        }

        private bool TryResolveLessonPlayer()
        {
            if (lessonPlayer == null)
                lessonPlayer = FindAnyObjectByType<LessonPlayer>();

            if (lessonPlayer == null)
            {
                Debug.LogError(
                    "LessonSelectionUI: LessonPlayer is not available.");
                return false;
            }

            return true;
        }

        public void RefreshLessons()
        {
            lessonEntries.Clear();
            dropdownOptions.Clear();

            string lessonsPath = Path.Combine(
                Application.streamingAssetsPath,
                LessonsFolderName);

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                ListAndroidLessons(lessonsPath);
#else
                if (Directory.Exists(lessonsPath))
                {
                    foreach (string file in Directory.GetFiles(lessonsPath))
                    {
                        AddLessonFile(file, Path.GetFileName(file));
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"LessonSelectionUI: Lessons folder not found at " +
                        $"{lessonsPath}.");
                }
#endif
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"LessonSelectionUI: Could not scan the Lessons " +
                    $"folder. {exception.Message}");
            }

            lessonEntries.Sort(
                (a, b) => string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.OrdinalIgnoreCase));

            foreach (LessonEntry entry in lessonEntries)
                dropdownOptions.Add(entry.DisplayName);

            UpdateDropdown();

            Debug.Log(
                $"LessonSelectionUI: Found {lessonEntries.Count} lesson(s).");
        }

        private void AddLessonFile(string path, string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (extension != ".mid" && extension != ".midi")
                return;

            string displayName = Path.GetFileNameWithoutExtension(fileName);

            if (string.IsNullOrEmpty(displayName))
                return;

            lessonEntries.Add(new LessonEntry
            {
                DisplayName = displayName.Replace('_', ' '),
                FileName = fileName,
                Path = path
            });
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void ListAndroidLessons(string lessonsPath)
        {
            try
            {
                using (AndroidJavaClass unityPlayer =
                       new AndroidJavaClass(
                           "com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity =
                       unityPlayer.GetStatic<AndroidJavaObject>(
                           "currentActivity"))
                using (AndroidJavaObject assets =
                       activity.Call<AndroidJavaObject>("getAssets"))
                {
                    string[] files =
                        assets.Call<string[]>("list", LessonsFolderName);

                    if (files == null)
                        return;

                    foreach (string file in files)
                    {
                        AddLessonFile(
                            lessonsPath + "/" + file,
                            file);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"LessonSelectionUI: Could not list Android " +
                    $"StreamingAssets. {exception.Message}");
            }
        }
#endif

        private void UpdateDropdown()
        {
            if (lessonDropdown == null)
                return;

            lessonDropdown.ClearOptions();
            lessonDropdown.AddOptions(dropdownOptions);

            if (dropdownOptions.Count > 0)
            {
                lessonDropdown.value = 0;
                lessonDropdown.RefreshShownValue();
            }

            if (startLessonButton != null)
                startLessonButton.interactable = dropdownOptions.Count > 0;
        }

        public void StartLesson()
        {
            if (isLoading)
                return;

            if (lessonEntries.Count == 0)
            {
                SetStatus("No MIDI lessons found.");
                return;
            }

            int index = lessonDropdown != null
                ? lessonDropdown.value
                : -1;

            if (index < 0 || index >= lessonEntries.Count)
            {
                SetStatus("Select a lesson first.");
                return;
            }

            if (!TryResolveLessonPlayer())
            {
                SetStatus("LessonPlayer was not found.");
                return;
            }

            StartCoroutine(LoadLessonRoutine(lessonEntries[index]));
        }

        public void PlayLesson()
        {
            if (!TryResolveLessonPlayer())
                return;

            if (lessonPlayer.CurrentLesson == null)
            {
                Debug.LogWarning(
                    "LessonSelectionUI: No lesson is loaded to play.");
                return;
            }

            lessonPlayer.Play();
            Debug.Log("LessonSelectionUI: Playback started.");
        }

        public void PauseLesson()
        {
            if (!TryResolveLessonPlayer())
                return;

            lessonPlayer.Pause();
            Debug.Log("LessonSelectionUI: Playback paused.");
        }

        public void ResumeLesson()
        {
            if (!TryResolveLessonPlayer())
                return;

            if (lessonPlayer.CurrentLesson == null)
            {
                Debug.LogWarning(
                    "LessonSelectionUI: No lesson is loaded to resume.");
                return;
            }

            lessonPlayer.Play();
            Debug.Log("LessonSelectionUI: Playback resumed.");
        }

        public void StopLesson()
        {
            if (!TryResolveLessonPlayer())
                return;

            lessonPlayer.Stop();
            Debug.Log("LessonSelectionUI: Playback stopped.");
        }

        private IEnumerator LoadLessonRoutine(LessonEntry entry)
        {
            isLoading = true;
            SetStartButtonInteractable(false);
            SetStatus($"Loading {entry.DisplayName}...");

            byte[] data = null;
            string error = null;

            using (UnityWebRequest request =
                   UnityWebRequest.Get(ToRequestUrl(entry.Path)))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    error =
                        $"Could not read {entry.FileName}: " +
                        $"{request.error}";
                }
                else
                {
                    data = request.downloadHandler.data;
                }
            }

            Lesson lesson = null;

            if (error == null && (data == null || data.Length == 0))
                error = $"{entry.FileName} is empty.";

            if (error == null)
            {
                try
                {
                    MIDIParser parser = new MIDIParser();
                    lesson = parser.ParseMIDIBytes(data, entry.FileName);
                }
                catch (Exception exception)
                {
                    error =
                        $"Could not parse {entry.FileName}: " +
                        $"{exception.Message}";
                }
            }

            if (error != null)
            {
                Debug.LogError($"LessonSelectionUI: {error}");
                SetStatus(error);
            }
            else if (lesson == null || lesson.Notes.Count == 0)
            {
                Debug.LogError(
                    $"LessonSelectionUI: {entry.FileName} contains " +
                    $"no notes.");
                SetStatus($"{entry.DisplayName} contains no notes.");
            }
            else
            {
                lessonPlayer.Stop();
                lessonPlayer.LoadLesson(lesson);
                lessonPlayer.Play();

                Debug.Log(
                    $"LessonSelectionUI: Playing '{lesson.Name}' " +
                    $"({lesson.Notes.Count} notes, " +
                    $"{lesson.GetDuration():F2}s).");

                SetStatus(
                    $"Playing {lesson.Name} " +
                    $"({lesson.Notes.Count} notes)");
            }

            isLoading = false;
            SetStartButtonInteractable(lessonEntries.Count > 0);
        }

        private static string ToRequestUrl(string path)
        {
            if (path.Contains("://"))
                return path;

            return new Uri(path).AbsoluteUri;
        }

        private void SetStartButtonInteractable(bool interactable)
        {
            if (startLessonButton != null)
                startLessonButton.interactable = interactable;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void ConfigureDropdownAppearance()
        {
            if (lessonDropdown == null)
                return;

            // Selected lesson label.
            if (lessonDropdown.captionText != null)
            {
                lessonDropdown.captionText.fontSize = 30f;
                lessonDropdown.captionText.fontStyle = FontStyles.Bold;
                lessonDropdown.captionText.alignment =
                    TextAlignmentOptions.Center;
                lessonDropdown.captionText.enableAutoSizing = false;
            }

            // Dropdown list items.
            if (lessonDropdown.itemText != null)
            {
                lessonDropdown.itemText.fontSize = 26f;
                lessonDropdown.itemText.fontStyle = FontStyles.Bold;
                lessonDropdown.itemText.alignment =
                    TextAlignmentOptions.Center;
                lessonDropdown.itemText.enableAutoSizing = false;
            }

            if (lessonDropdown.template != null)
            {
                RectTransform templateRect =
                    lessonDropdown.template.GetComponent<RectTransform>();

                if (templateRect != null)
                    templateRect.sizeDelta = new Vector2(
                        templateRect.sizeDelta.x, 260f);

                Transform item =
                    lessonDropdown.template.Find("Viewport/Content/Item");

                if (item != null)
                {
                    RectTransform itemRect =
                        item.GetComponent<RectTransform>();

                    if (itemRect != null)
                        itemRect.sizeDelta = new Vector2(
                            itemRect.sizeDelta.x, 48f);

                    LayoutElement layout =
                        item.GetComponent<LayoutElement>();

                    if (layout != null)
                        layout.preferredHeight = 48f;
                }
            }
        }

    }
}
