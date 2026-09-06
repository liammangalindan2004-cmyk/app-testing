using UnityEngine;

public class TaskButton : MonoBehaviour
{
    public TaskManager taskManager;
    public void OnTaskComplete()
    {
        if (taskManager != null)
        {
            taskManager.CompleteTask();
        }
    }
}