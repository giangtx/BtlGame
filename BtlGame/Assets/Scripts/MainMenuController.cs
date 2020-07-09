using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    public void PlayGame()
    {
        Application.LoadLevel("Main");
    }
    public void Screan2()
    {
        Application.LoadLevel("Main2");
    }
    public void Screan3()
    {
        Application.LoadLevel("Main3");
    }

}
