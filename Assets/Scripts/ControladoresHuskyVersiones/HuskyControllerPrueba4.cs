using UnityEngine;

public class HuskyManualController4 : MonoBehaviour
{
    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;

    [Header("Parámetros de Movimiento")]
    public float maxLinearSpeed = 1.5f; // m/s
    public float maxAngularSpeed = 2.0f; // rad/s (Aumentado para permitir giros más cerrados)

    [Header("Geometría del Robot")]
    public float trackWidth = 0.55f; // Distancia entre las ruedas izquierdas y derechas (aprox en el Husky)
    public float wheelRadius = 0.165f; // Radio de la rueda en metros

    [Header("Suavizado")]
    public float inputSmoothTime = 0.1f;
    private float currentMove, currentTurn;
    private float moveVelocity, turnVelocity;

    void Start()
    {
        ArticulationBody root = GetComponent<ArticulationBody>();
        if (root != null)
        {
            // Bajar el centro de masa mejora la estabilidad en terrenos irregulares
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
            drive.damping = 100f; // Aumentado ligeramente para mejor agarre sin vibraciones
            drive.forceLimit = 1000f; // Limitado a un par motor realista (no 100000)
            wheel.xDrive = drive;
        }
    }

    void FixedUpdate()
    {
        float targetMove = Input.GetAxis("Vertical");
        float targetTurn = Input.GetAxis("Horizontal");

        // 1. Suavizado de Inputs
        currentMove = Mathf.SmoothDamp(currentMove, targetMove, ref moveVelocity, inputSmoothTime);
        currentTurn = Mathf.SmoothDamp(currentTurn, targetTurn, ref turnVelocity, inputSmoothTime);

        // 2. Cálculo cinemático diferencial estándar
        float desiredLinear = -currentMove * maxLinearSpeed;
        float desiredAngular = currentTurn * maxAngularSpeed;

        // Ecuaciones de tracción diferencial: V_rueda = V_lineal +/- (V_angular * AnchoVia / 2)
        // Nota: El signo de desiredLinear puede necesitar invertirse a negativo si tu URDF mira hacia -Z
        float leftVelocityMPS = desiredLinear + (desiredAngular * (trackWidth / 2f));
        float rightVelocityMPS = desiredLinear - (desiredAngular * (trackWidth / 2f));

        // 3. Normalización si superamos los límites (evita recortar el giro con Clamp)
        float maxCalculatedVel = Mathf.Max(Mathf.Abs(leftVelocityMPS), Mathf.Abs(rightVelocityMPS));
        if (maxCalculatedVel > maxLinearSpeed + (maxAngularSpeed * trackWidth / 2f))
        {
            float scale = (maxLinearSpeed + (maxAngularSpeed * trackWidth / 2f)) / maxCalculatedVel;
            leftVelocityMPS *= scale;
            rightVelocityMPS *= scale;
        }

        // 4. Aplicar velocidades
        AplicarVelocidadAngular(leftWheels, leftVelocityMPS);
        AplicarVelocidadAngular(rightWheels, rightVelocityMPS);
    }

    void AplicarVelocidadAngular(ArticulationBody[] wheels, float linearVelocityMPS)
    {
        // Convertimos velocidad lineal (m/s) a velocidad angular (grados/s) para el xDrive
        float targetAngularVelocityDeg = (linearVelocityMPS / wheelRadius) * Mathf.Rad2Deg;

        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = targetAngularVelocityDeg;
            wheel.xDrive = drive;
        }
    }
}