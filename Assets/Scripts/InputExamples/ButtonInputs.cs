using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Liminal.SDK.VR;
using Liminal.SDK.VR.Input;

public class ButtonInputs : MonoBehaviour
{
    [SerializeField] ParticleSystem particles;
    [SerializeField] TrailRenderer trails;
 
    // Update is called once per frame
    void Update()
    {
        //get the primary input
        var primaryInput = VRDevice.Device.PrimaryInputDevice;

        if (primaryInput.GetButtonDown(VRButton.One) || Input.GetMouseButtonDown(0))
        {
            particles.Play();
            trails.emitting = true;
        }
        if (primaryInput.GetButtonUp(VRButton.One) || Input.GetMouseButtonUp(0))
        {
            particles.Play();
            trails.emitting = true;
        }
    }
}
