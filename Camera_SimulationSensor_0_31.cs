using UnityEngine;

public class Camera_SimulationSensor_0_31 : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Normal motion, scene units per second (1 unit = 10 mm).")]
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("Motion while Left Shift is held, scene units per second.")]
    [SerializeField] private float fastMoveSpeed = 5f;
    [SerializeField] private float lookSpeed = 2f;

    private float _rotationX = 0f;
    private float _rotationY = 0f;

    private void Start()
    {
        // Initialize internal rotations based on current camera starting angle
        Vector3 euler = transform.localRotation.eulerAngles;
        _rotationX = euler.y;
        _rotationY = -euler.x;
    }

    private void Update()
    {
        // --- 1. Rotation (Only when holding Right Click) ---
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _rotationX += Input.GetAxis("Mouse X") * lookSpeed;
            _rotationY += Input.GetAxis("Mouse Y") * lookSpeed;
            _rotationY = Mathf.Clamp(_rotationY, -90f, 90f);

            transform.localRotation = Quaternion.Euler(-_rotationY, _rotationX, 0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // --- 2. Translation (Keyboard inputs) ---
        Vector3 moveDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) moveDirection += transform.forward;
        if (Input.GetKey(KeyCode.S)) moveDirection -= transform.forward;
        if (Input.GetKey(KeyCode.D)) moveDirection += transform.right;
        if (Input.GetKey(KeyCode.A)) moveDirection -= transform.right;
        
        // Vertical actions
        if (Input.GetKey(KeyCode.E)) moveDirection += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) moveDirection += Vector3.down;

        // Apply spatial movement over frame ticks
        if (moveDirection != Vector3.zero)
        {
            float speed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;
            transform.position += moveDirection.normalized * (speed * Time.deltaTime);
        }
    }
}
