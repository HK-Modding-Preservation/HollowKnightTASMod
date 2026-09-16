using System;
using System.Globalization;
using HollowKnightTAS.Core.Automation;
using UnityEngine;

namespace HollowKnightTAS.Runtime.Automation.Mutation
{
    public sealed class HeroPoseMutationAdapter
    {
        public StateMutationApplication Apply(
            float positionX,
            float positionY,
            float velocityX,
            float velocityY)
        {
            ValidateFinite(positionX, nameof(positionX));
            ValidateFinite(positionY, nameof(positionY));
            ValidateFinite(velocityX, nameof(velocityX));
            ValidateFinite(velocityY, nameof(velocityY));
            if (Math.Abs(positionX)
                > StateMutationBounds.MaximumAbsolutePosition
                || Math.Abs(positionY)
                > StateMutationBounds.MaximumAbsolutePosition
                || Math.Abs(velocityX)
                > StateMutationBounds.MaximumAbsoluteVelocity
                || Math.Abs(velocityY)
                > StateMutationBounds.MaximumAbsoluteVelocity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionX),
                    "Hero pose exceeds protocol v1 bounds.");
            }

            var hero = HeroController.SilentInstance
                       ?? throw new InvalidOperationException(
                           "An active HeroController is unavailable.");
            if (!hero.gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "HeroController is not active.");
            }

            var body = hero.GetComponent<Rigidbody2D>()
                       ?? throw new InvalidOperationException(
                           "Hero Rigidbody2D is unavailable.");
            var beforePosition = hero.transform.position;
            var beforeVelocity = body.velocity;
            var beforeCurrentVelocity = hero.current_velocity;
            var delta = new Vector2(
                positionX - beforePosition.x,
                positionY - beforePosition.y);
            if (delta.magnitude
                > StateMutationBounds.MaximumPositionDelta)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionX),
                    "Hero pose cannot move more than 20 world units.");
            }

            try
            {
                hero.transform.position = new Vector3(
                    positionX,
                    positionY,
                    beforePosition.z);
                var velocity = new Vector2(velocityX, velocityY);
                body.velocity = velocity;
                hero.current_velocity = velocity;
            }
            catch
            {
                hero.transform.position = beforePosition;
                body.velocity = beforeVelocity;
                hero.current_velocity = beforeCurrentVelocity;
                throw;
            }

            return new StateMutationApplication(
                "position=("
                + beforePosition.x.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ","
                + beforePosition.y.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ")->("
                + positionX.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ","
                + positionY.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ");velocity=("
                + beforeVelocity.x.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ","
                + beforeVelocity.y.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ")->("
                + velocityX.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ","
                + velocityY.ToString(
                    "R",
                    CultureInfo.InvariantCulture)
                + ")",
                () =>
                {
                    hero.transform.position = beforePosition;
                    body.velocity = beforeVelocity;
                    hero.current_velocity = beforeCurrentVelocity;
                });
        }

        private static void ValidateFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    "Mutation values must be finite.");
            }
        }
    }
}
