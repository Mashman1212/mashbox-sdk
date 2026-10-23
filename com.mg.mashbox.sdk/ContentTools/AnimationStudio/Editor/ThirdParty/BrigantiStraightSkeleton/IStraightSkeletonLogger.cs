
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace MashBoxSDK.AnimationStudio.ThirdParty.StraightSkeleton
{
	public interface IStraightSkeletonLogger
	{
		void Log(string message, int depth);
	}
}