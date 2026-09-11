using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public interface IShootable
    {
        void OnShot(Vector3 point, Vector3 direction, float impulse);
    }
}
