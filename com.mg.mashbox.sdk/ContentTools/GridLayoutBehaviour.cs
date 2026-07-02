using UnityEngine;

namespace MashBoxSDK.ContentTools
{
    [ExecuteInEditMode]
    public class GridLayoutBehaviour : MonoBehaviour
    {
        [SerializeField] 
        [Range(1,100)]
        private int _columns = 1;
        
        [SerializeField] 
        [Range(1,100)]
        private int _rows = 60;
 
        [SerializeField] 
        [Range(0.0f,1.0f)]
        private float _spacing = .1f;
        
        [Header("Offset")]
        public Vector2 offset = Vector2.zero;
        
        [Range(-180f, 180f)]
        public float _rotateX = 0f;

        [Range(-180f, 180f)]
        public float _rotateY = 0f;

        [Range(-180f, 180f)]
        public float _rotateZ = 0f;

        public void LayoutChildren()
        {
            int childCount = transform.childCount;
            if (childCount == 0) return;

            Vector3 startingPosition = new Vector3(offset.x, offset.y, 0);
            Quaternion rot = Quaternion.Euler(_rotateX, _rotateY, _rotateZ);

            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);

                int column = i % _columns;
                int row = i / _columns;

                Vector3 position = startingPosition + new Vector3(
                    row * _spacing,
                    -column * _spacing,
                    0
                );

                child.localPosition = position;
                child.localRotation = rot;
            }
        }

        void Update()
        {
            LayoutChildren();
        }
    }
}