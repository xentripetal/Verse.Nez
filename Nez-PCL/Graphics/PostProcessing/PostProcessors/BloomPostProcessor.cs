﻿using System;
using Nez;
using Nez.Textures;
using Microsoft.Xna.Framework.Graphics;
using Nez.Systems;
using Microsoft.Xna.Framework;


namespace Nez
{
	public class BloomPostProcessor : PostProcessor
	{
		/// <summary>
		/// the settings used by the bloom and blur shaders
		/// </summary>
		public BloomSettings settings;

		/// <summary>
		/// scale of the internal RenderTargets. For high resolution renders a half sized RT is usually more than enough.
		/// </summary>
		public int renderTargetScale = 1;

		Effect _bloomExtractEffect;
		Effect _bloomCombineEffect;
		Effect _gaussianBlurEffect;

		RenderTarget2D _renderTarget1;
		RenderTarget2D _renderTarget2;

		
		public BloomPostProcessor( int executionOrder ) : base( executionOrder )
		{
			settings = BloomSettings.presetSettings[3];
		}


		public override void onAddedToScene()
		{
			_bloomExtractEffect = scene.contentManager.LoadEffect<Effect>( "bloomExtract", EffectResource.bloomExtractBytes );
			_bloomCombineEffect = scene.contentManager.LoadEffect<Effect>( "bloomCombine", EffectResource.bloomCombineBytes );
			_gaussianBlurEffect = scene.contentManager.LoadEffect<Effect>( "gaussianBlur", EffectResource.gaussianBlurBytes );
		}


		public override void onSceneBackBufferSizeChanged( int newWidth, int newHeight )
		{
			// Create two rendertargets for the bloom processing. These are half the size of the backbuffer, in order to minimize fillrate costs. Reducing
			// the resolution in this way doesn't hurt quality, because we are going to be blurring the bloom images in any case.
			// the demo uses a tiny backbuffer so no need to reduce size any further
			newWidth *= renderTargetScale;
			newHeight *= renderTargetScale;

			_renderTarget1 = new RenderTarget2D( Core.graphicsDevice, newWidth, newHeight, false, Screen.backBufferFormat, DepthFormat.None );
			_renderTarget2 = new RenderTarget2D( Core.graphicsDevice, newWidth, newHeight, false, Screen.backBufferFormat, DepthFormat.None );
		}


		/// <summary>
		/// Computes sample weightings and texture coordinate offsets
		/// for one pass of a separable gaussian blur filter.
		/// </summary>
		void setBlurEffectParameters( float dx, float dy )
		{
			// Look up the sample weight and offset effect parameters.
			EffectParameter weightsParameter, offsetsParameter;
			weightsParameter = _gaussianBlurEffect.Parameters["SampleWeights"];
			offsetsParameter = _gaussianBlurEffect.Parameters["SampleOffsets"];

			// Look up how many samples our gaussian blur effect supports.
			int sampleCount = weightsParameter.Elements.Count;

			// Create temporary arrays for computing our filter settings.
			var sampleWeights = new float[sampleCount];
			var sampleOffsets = new Vector2[sampleCount];

			// The first sample always has a zero offset.
			sampleWeights[0] = computeGaussian( 0 );
			sampleOffsets[0] = new Vector2( 0 );

			// Maintain a sum of all the weighting values.
			var totalWeights = sampleWeights[0];

			// Add pairs of additional sample taps, positioned along a line in both directions from the center.
			for( var i = 0; i < sampleCount / 2; i++ )
			{
				// Store weights for the positive and negative taps.
				var weight = computeGaussian( i + 1 );

				sampleWeights[i * 2 + 1] = weight;
				sampleWeights[i * 2 + 2] = weight;

				totalWeights += weight * 2;

				// To get the maximum amount of blurring from a limited number of pixel shader samples, we take advantage of the bilinear filtering
				// hardware inside the texture fetch unit. If we position our texture coordinates exactly halfway between two texels, the filtering unit
				// will average them for us, giving two samples for the price of one. This allows us to step in units of two texels per sample, rather
				// than just one at a time. The 1.5 offset kicks things off by positioning us nicely in between two texels.
				var sampleOffset = i * 2 + 1.5f;

				var delta = new Vector2( dx, dy ) * sampleOffset;

				// Store texture coordinate offsets for the positive and negative taps.
				sampleOffsets[i * 2 + 1] = delta;
				sampleOffsets[i * 2 + 2] = -delta;
			}

			// Normalize the list of sample weightings, so they will always sum to one.
			for( int i = 0; i < sampleWeights.Length; i++ )
			{
				sampleWeights[i] /= totalWeights;
			}

			// Tell the effect about our new filter settings.
			weightsParameter.SetValue( sampleWeights );
			offsetsParameter.SetValue( sampleOffsets );
		}


		/// <summary>
		/// Evaluates a single point on the gaussian falloff curve.
		/// Used for setting up the blur filter weightings.
		/// </summary>
		float computeGaussian( float n )
		{
			var theta = settings.blurAmount;

			return (float)( ( 1.0 / Math.Sqrt( 2 * Math.PI * theta ) ) *
			Math.Exp( -( n * n ) / ( 2 * theta * theta ) ) );
		}


		public override void process( RenderTarget2D source, RenderTarget2D destination )
		{
			Core.graphicsDevice.SamplerStates[1] = SamplerState.LinearClamp;

			// Pass 1: draw the scene into rendertarget 1, using a shader that extracts only the brightest parts of the image.
			_bloomExtractEffect.Parameters["BloomThreshold"].SetValue( settings.threshold );
			drawFullscreenQuad( source, _renderTarget1, _bloomExtractEffect );

			// Pass 2: draw from rendertarget 1 into rendertarget 2, using a shader to apply a horizontal gaussian blur filter.
			setBlurEffectParameters( 1.0f / (float)_renderTarget1.Width, 0 );
			drawFullscreenQuad( _renderTarget1, _renderTarget2, _gaussianBlurEffect );

			// Pass 3: draw from rendertarget 2 back into rendertarget 1, using a shader to apply a vertical gaussian blur filter.
			setBlurEffectParameters( 0, 1.0f / (float)_renderTarget1.Height );
			drawFullscreenQuad( _renderTarget2, _renderTarget1, _gaussianBlurEffect );

			// Pass 4: draw both rendertarget 1 and the original scene image back into the main backbuffer, using a shader that
			// combines them to produce the final bloomed result.
			var parameters = _bloomCombineEffect.Parameters;

			parameters["BloomIntensity"].SetValue( settings.intensity );
			parameters["BaseIntensity"].SetValue( settings.baseIntensity );
			parameters["BloomSaturation"].SetValue( settings.saturation );
			parameters["BaseSaturation"].SetValue( settings.baseSaturation );

			Core.graphicsDevice.Textures[1] = source;

			drawFullscreenQuad( _renderTarget1, destination, _bloomCombineEffect );
		}


		public override void unload()
		{
			_renderTarget1.Dispose();
			_renderTarget2.Dispose();
		}

	}
}
