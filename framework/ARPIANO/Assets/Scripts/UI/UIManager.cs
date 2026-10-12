
using UnityEngine;

namespace ARPIANO.Scripts.UI
{
    public class UIManager : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject calibrationPanel;
        [SerializeField] private GameObject modeSelectionPanel;
        [SerializeField] private GameObject livePerformancePanel;
        [SerializeField] private GameObject lessonPanel;
        [SerializeField] private ARPIANO.Scripts.Tracking.KeyboardCalibrationController calibrationController;

        public void ShowLivePerformance()
        {
            SetActivePanel(livePerformancePanel);
        }

        public void ShowLesson()
        {
            SetActivePanel(lessonPanel);
        }

        public void ShowModeSelection()
        {
            SetActivePanel(modeSelectionPanel);
        }

        public void ShowCalibration()
        {
            Debug.Log("UIManager: ShowCalibration() called.");
            Debug.Log($"Calibration controller assigned: {calibrationController != null}");

            SetActivePanel(calibrationPanel);

            if (calibrationController != null)
            {
                calibrationController.ResetCalibration();
            }
            else
            {
                Debug.LogError(
                    "UIManager: KeyboardCalibrationController is not assigned!"
                );
            }
        }

        private void SetActivePanel(GameObject panelToShow)
        {
            if (calibrationPanel != null)
                calibrationPanel.SetActive(false);

            if (modeSelectionPanel != null)
                modeSelectionPanel.SetActive(false);

            if (livePerformancePanel != null)
                livePerformancePanel.SetActive(false);

            if (lessonPanel != null)
                lessonPanel.SetActive(false);

            if (panelToShow != null)
                panelToShow.SetActive(true);
        }
    }
}
