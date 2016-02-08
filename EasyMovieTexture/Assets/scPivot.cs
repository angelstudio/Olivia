using UnityEngine;
using System.Collections;

public class scPivot : MonoBehaviour {

	public float rotY=0f;

	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	rotY=transform.eulerAngles.y;

	transform.Rotate(-Vector3.up * Time.deltaTime*10f);
	}
}
