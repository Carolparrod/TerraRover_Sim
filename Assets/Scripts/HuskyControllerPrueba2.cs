using UnityEngine;

public class HuskyControllerPrueba2 : MonoBehaviour
{
    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;

    [Header("Parámetros de Movimiento")]
    public float maxLinearSpeed = 1.0f;
    //public float maxAngularSpeed = 2.0f; // Usaremos esto para normalizar el giro

    public float maxAngularSpeed = 0.22f;

    void Start()
    {
        ArticulationBody root = GetComponent<ArticulationBody>();
        if (root != null)
        {
            // Bajamos un poco menos el centro de masa para evitar que el 
            // colisionador de la base choque con piedras altas
            root.centerOfMass = new Vector3(0, -0.1f, 0);
        }

        ConfigurarMotores(leftWheels);
        ConfigurarMotores(rightWheels);
    }

    void ConfigurarMotores(ArticulationBody[] wheels)
    {
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.stiffness = 0f;
            // BAJAMOS el Damping. 10-50 es suficiente para estabilidad sin bloquear
            drive.damping = 50f;
            drive.forceLimit = 100000f;
            wheel.xDrive = drive;
        }
    }

    void FixedUpdate()
    {
        float move = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");

        // MEZCLA DIFERENCIAL MEJORADA:
        // Calculamos la velocidad deseada para cada lado de forma independiente
        // Sumar y restar asegura que el giro no "cancele" el movimiento en terreno difícil
        // Invertimos el signo de 'move' (ponemos un - delante)
        float leftVelocity = (-move * maxLinearSpeed) - (-turn * maxAngularSpeed);
        float rightVelocity = (-move * maxLinearSpeed) + (-turn * maxAngularSpeed);
        //float leftVelocity = (move * maxLinearSpeed) - (turn * maxAngularSpeed);
        //float rightVelocity = (move * maxLinearSpeed) + (turn * maxAngularSpeed);

        // Opcional: Limitar la velocidad máxima combinada para que no "explote" la física
        leftVelocity = Mathf.Clamp(leftVelocity, -maxLinearSpeed, maxLinearSpeed);
        rightVelocity = Mathf.Clamp(rightVelocity, -maxLinearSpeed, maxLinearSpeed);

        AplicarVelocidad(leftWheels, leftVelocity);
        AplicarVelocidad(rightWheels, rightVelocity);

        //Monitoreo de velocidad angular
        if (Input.GetKey(KeyCode.Space)) // Presiona Espacio para ver el reporte
        {
            Debug.Log($"IZQ: {leftWheels[0].angularVelocity.x:F2} rad/s | DER: {rightWheels[0].angularVelocity.x:F2} rad/s");
        }
    }

    void AplicarVelocidad(ArticulationBody[] wheels, float velocity)
    {
        // Radio de la rueda del Husky es aprox 0.165m
        float wheelRadius = 0.165f;

        // Convertimos velocidad lineal (m/s) a angular (deg/s) correctamente:
        // v = w * r  => w = v / r
        float targetAngularVelocity = (velocity / wheelRadius) * Mathf.Rad2Deg;

        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = targetAngularVelocity;
            wheel.xDrive = drive;
        }
    }
}
