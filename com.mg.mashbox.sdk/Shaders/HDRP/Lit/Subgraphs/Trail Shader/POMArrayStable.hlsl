#ifndef POM_ARRAY_STABLE_INCLUDED
#define POM_ARRAY_STABLE_INCLUDED

void POMArrayStable_float(
    UnityTexture2DArray HeightArray,
    float Slice,
    float2 UV,
    float3 ViewDirectionTS,
    float3 ViewDirectionWS,
    float IsPlanar,
    float PlanarAmplitudeScale,
    float Amplitude,
    float Steps,
    float InvertHeight,
    float CameraDistance,
    float FadeStart,
    float FadeEnd,
    out float2 ParallaxUV)
{
    // Explicit POM-off states.
    if (Steps < 1.0 || abs(Amplitude) < 0.000001)
    {
        ParallaxUV = UV;
        return;
    }

    // Near = 0, far = 1.
    float distanceTransition = 0.0;

    if (FadeEnd > FadeStart)
    {
        distanceTransition = smoothstep(
            FadeStart,
            FadeEnd,
            CameraDistance
        );
    }

    // Tangent-space direction for regular mesh UV mapping.
    float3 tangentViewDirection =
        normalize(ViewDirectionTS);

    // Projection-space direction for world-aligned XZ planar mapping:
    // World X → U
    // World Z → V
    // World Y → projection depth
    float3 planarViewDirection = normalize(float3(
        ViewDirectionWS.x,
        ViewDirectionWS.z,
        ViewDirectionWS.y
    ));

    float3 viewDirection =
        IsPlanar > 0.5
        ? planarViewDirection
        : tangentViewDirection;

    // Fixed configured steps nearby, transitioning to one step at distance.
    int configuredSteps =
        clamp((int)round(Steps), 1, 64);

    int stepCount = max(1, (int)round(lerp(
        (float)configuredSteps,
        1.0,
        distanceTransition
    )));

    float sliceIndex =
        round(Slice);

    // Prevent division by zero at perfectly grazing angles.
    float viewZ =
        max(abs(viewDirection.z), 0.05);

    float2 viewSlope =
        viewDirection.xy / viewZ;

    // World-planar POM becomes unstable as the camera direction becomes
    // parallel to the XZ projection plane. Soft-limit the slope without
    // abruptly enabling or disabling POM.
    if (IsPlanar > 0.5)
    {
        const float planarMaximumSlope = 2.0;

        float slopeLength =
            length(viewSlope);

        if (slopeLength > 0.00001)
        {
            float limitedLength =
                (slopeLength * planarMaximumSlope) /
                (slopeLength + planarMaximumSlope);

            viewSlope *=
                limitedLength / slopeLength;
        }
    }

    float modeAmplitudeScale = lerp(
        1.0,
        PlanarAmplitudeScale,
        saturate(IsPlanar)
    );

    // Near = normal amplitude, far = twice the amplitude.
    float effectiveAmplitude =
        Amplitude *
        modeAmplitudeScale *
        lerp(1.0, 2.0, distanceTransition);

    float2 rayOffset =
        viewSlope * effectiveAmplitude;

    float layerStep =
        1.0 / (float)stepCount;

    float2 uvStep =
        rayOffset / (float)stepCount;

    float2 currentUV = UV;
    float currentLayerDepth = 0.0;

    float currentHeight = SAMPLE_TEXTURE2D_ARRAY(
        HeightArray.tex,
        HeightArray.samplerstate,
        currentUV,
        sliceIndex
    ).r;

    if (InvertHeight > 0.5)
    {
        currentHeight =
            1.0 - currentHeight;
    }

    [loop]
    for (int i = 0; i < stepCount; i++)
    {
        if (currentLayerDepth >= currentHeight)
        {
            break;
        }

        currentUV -= uvStep;
        currentLayerDepth += layerStep;

        currentHeight = SAMPLE_TEXTURE2D_ARRAY(
            HeightArray.tex,
            HeightArray.samplerstate,
            currentUV,
            sliceIndex
        ).r;

        if (InvertHeight > 0.5)
        {
            currentHeight =
                1.0 - currentHeight;
        }
    }

    // The final two coarse positions bracket the surface intersection.
    float2 upperUV =
        currentUV + uvStep;

    float upperDepth =
        max(currentLayerDepth - layerStep, 0.0);

    float2 lowerUV =
        currentUV;

    float lowerDepth =
        currentLayerDepth;

    // Four refinements nearby, transitioning to none at long range.
    int refinementCount = (int)round(lerp(
        4.0,
        0.0,
        distanceTransition
    ));

    [loop]
    for (
        int refinement = 0;
        refinement < refinementCount;
        refinement++)
    {
        float2 middleUV =
            (upperUV + lowerUV) * 0.5;

        float middleDepth =
            (upperDepth + lowerDepth) * 0.5;

        float middleHeight = SAMPLE_TEXTURE2D_ARRAY(
            HeightArray.tex,
            HeightArray.samplerstate,
            middleUV,
            sliceIndex
        ).r;

        if (InvertHeight > 0.5)
        {
            middleHeight =
                1.0 - middleHeight;
        }

        if (middleDepth < middleHeight)
        {
            upperUV = middleUV;
            upperDepth = middleDepth;
        }
        else
        {
            lowerUV = middleUV;
            lowerDepth = middleDepth;
        }
    }

    ParallaxUV =
        (upperUV + lowerUV) * 0.5;
}

#endif