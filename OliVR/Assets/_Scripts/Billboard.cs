using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;


public class Billboard : MonoBehaviour {

    void Update() {
        transform.LookAt(Camera.main.transform.position, -Vector3.up);
    }

}