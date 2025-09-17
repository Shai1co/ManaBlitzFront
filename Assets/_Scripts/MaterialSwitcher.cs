using UnityEngine;
using Sirenix.OdinInspector;

public class MaterialSwitcher : MonoBehaviour
{
    [SerializeField] private Material mat1;
    [SerializeField] private Material mat2;

    private Renderer _renderer;
    private int currentMaterialIndex = 0; // 0 for mat1, 1 for mat2

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }
    [Button]
    public void SwitchTo(int type)
    {
        if (_renderer == null) Awake();
        if (_renderer == null) return;

        switch (type)
        {
            case 0:
                if (mat1 != null) 
                {
                    _renderer.sharedMaterial = mat1;
                    currentMaterialIndex = 0;
                }
                break;
            case 1:
                if (mat2 != null) 
                {
                    _renderer.sharedMaterial = mat2;
                    currentMaterialIndex = 1;
                }
                break;
        }
    }

    [Button]
    public void Toggle()
    {
        if (_renderer == null) Awake();
        if (_renderer == null) return;

        if (currentMaterialIndex == 0 && mat2 != null)
        {
            _renderer.sharedMaterial = mat2;
            currentMaterialIndex = 1;
        }
        else if (currentMaterialIndex == 1 && mat1 != null)
        {
            _renderer.sharedMaterial = mat1;
            currentMaterialIndex = 0;
        }
    }

}
