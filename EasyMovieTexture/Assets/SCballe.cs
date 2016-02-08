using UnityEngine;
using System.Collections;

public class SCballe : MonoBehaviour {


	public float counter=0f;

	public GameObject looker;

	public float forcePower = 5f;

	public float r=0f;
	public float g=0f;
	public float b=1f;

	public Color balleColor;

	public GameObject lumiere;
		
	// Use this for initialization
	void Start () {
		r = Random.Range (0f, 1f);
		g = Random.Range (0f, 1f);
		b = Random.Range (0f, 1f);
		balleColor = new Color (r, g, b);

		GetComponent<Renderer> ().material.color = balleColor;
		GetComponent<Renderer> ().material.SetColor ("_EmissionColor", balleColor);
		lumiere.GetComponent<Light> ().color = balleColor; 

	}

	// Update is called once per frame
	void Update () {

		counter += Time.deltaTime;
		if(counter>=15f) {
			//counter = 0f;
			Destroy (gameObject);
		}

	}

	void FixedUpdate(){
		Vector3 dir =looker.transform.TransformDirection (Vector3.forward);
		GetComponent<Rigidbody> ().AddForce (dir);
}

}