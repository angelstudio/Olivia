using UnityEngine;
using System.Collections;

public class SCdistributeur : MonoBehaviour {

	public float ypos = 5f;
	public float xpos = 0f;
	public float zpos = 0f;
	public float range = 10f;

	public float counter=0f;

	public GameObject balleObj;

	// Use this for initialization
	void Start () {

	}

	// Update is called once per frame
	void Update () {

		counter += Time.deltaTime;
		if(counter>=1f) {
		xpos = Random.Range (-range, range);
		zpos = Random.Range (-range, range);

		transform.position=new Vector3(xpos,ypos,zpos);
			Instantiate (balleObj, transform.position, Quaternion.identity);
			counter = 0f;
		}
	
	}
}
