using UnityEngine;

public class TaskManager : MonoBehaviour
{
    [Header("References")]
    public ProgressCircle progressCircle;

    [Header("Task Settings")]
    public int totalTasks = 10;
    private int tasksCompleted = 0;

    void Start()
    {
        // Start at 0% progress
        if (progressCircle != null)
        {
            progressCircle.SetProgress(0f);
        }
    }
    
    public void CompleteTask()
    {
        tasksCompleted++;
        if (tasksCompleted > totalTasks)
        {
            tasksCompleted = totalTasks;
        }
        float newProgress = (float)tasksCompleted / (float)totalTasks;
        // Update the circle
        if (progressCircle != null)
        {
            progressCircle.SetProgress(newProgress);
        }
        Debug.Log($"Tasks completed: {tasksCompleted}/{totalTasks} ({newProgress * 100}%)");
        if (tasksCompleted >= totalTasks)
        {
            Debug.Log("All tasks completed!");
        }
    }
    
    public void ResetProgress()
    {
        tasksCompleted = 0;
        if (progressCircle != null)
        {
            progressCircle.SetProgress(0f);
        }
        Debug.Log("Progress reset!");
    }
}