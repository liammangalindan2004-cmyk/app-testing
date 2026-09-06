using UnityEngine;
using System.Collections;

public class slideIn : MonoBehaviour
{
    public Vector2 hidden;
    public Vector2 show;
    public float duration = .3f;
    bool isOpen = false;

        void Start()
    {
        transform.position = hidden;
    }

   public void toggle()
    {
        StopAllCoroutines();
        StartCoroutine(slide(isOpen ? hidden : show));
        isOpen = !isOpen; 
    }
    
    IEnumerator slide(Vector2 target)
    {
        Vector2 star = transform.position;
        float t = 0f;

        while (t < duration)
        {
            float smooth = Mathf.SmoothStep(0, 1, t / duration);
            transform.position = Vector2.Lerp(star, target, smooth);
            t += Time.deltaTime;
            yield return null;
        }
        transform.position = target;
    }
}
