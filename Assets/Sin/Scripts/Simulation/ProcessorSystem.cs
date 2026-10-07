using System.Collections.Generic;
using Factory.Data;

namespace Factory.Simulation
{
    // 제련로/성형기/합성기 공용 처리 시스템. RecipeRuntime 데이터를 그대로 읽어 소비/생산할 뿐
    // 레시피 id로 분기하지 않는다 — 새 레시피를 데이터로 추가해도 이 코드는 그대로 통과한다.
    public sealed class ProcessorSystem
    {
        public void Tick(float deltaSeconds, GameDatabase database, List<ProcessorInstance> processors,
            FactoryStatistics statistics = null)
        {
            for (int i = 0; i < processors.Count; i++)
            {
                var processor = processors[i];
                if (processor == null) continue; // 철거로 비워진 슬롯(SimulationWorld.RemoveProcessor 참고).

                if (!processor.IsProcessing)
                {
                    if (processor.RecipeId < 0) continue;
                    // 전력 없으면 새 사이클을 아예 시작하지 않는다(재료 소비 안 함) — 안 그러면
                    // "사이클 시작(=재료 소비)"은 전력과 무관하게 일어나고 "진행(Progress 증가)"만
                    // 전력에 묶여서, 전력이 없어도 레시피를 새로 설정할 때마다(또는 다음 틱에)
                    // 재료를 한 번씩 공짜로 먹어버리고 그 자리에 얼어붙는 버그가 생긴다(실제로
                    // 겪음: 전력 없이 레시피만 눌러도 코어/벨트에서 재료가 빨려 들어감).
                    if (!processor.IsPowered) continue;
                    var recipe = database.Recipes[processor.RecipeId];
                    // 산출물을 담을 자리가 없으면 사이클을 시작조차 하지 않는다 — 시작하면
                    // TryConsumeInputs가 입력을 소비해버리는데, 산출은 Capacity에서 잘려
                    // 증발하기 때문(출력 벨트 없음/막힘으로 OutputBuffer가 꽉 찬 상황).
                    if (!HasOutputSpace(processor, recipe)) continue;
                    if (!TryConsumeInputs(processor, recipe, statistics)) continue;
                    processor.ActiveRecipeId = processor.RecipeId;
                    processor.IsProcessing = true;
                    processor.Progress = 0f;
                }

                processor.Progress += deltaSeconds * processor.SpeedMultiplier;

                // while로 돌아 사이클 경계를 넘는 초과분(overshoot)을 다음 사이클로 이어간다
                // (MinerSystem.Tick과 동일한 이유) — 단, 매 사이클은 그 사이클이 실제로 소비한
                // ActiveRecipeId 기준으로 산출해야 한다. RecipeId가 처리 도중 바뀌었어도 이번
                // 사이클은 ActiveRecipeId로 끝맺고, 다음 사이클을 시작할 때 재료가 있으면
                // (바뀌었을 수도 있는) RecipeId로 새로 시작한다.
                while (processor.IsProcessing)
                {
                    var activeRecipe = database.Recipes[processor.ActiveRecipeId];
                    if (processor.Progress < activeRecipe.ProcessSeconds) break;

                    // 사이클은 끝났지만 산출물 자리가 없다 — 완성품을 들고 대기한다.
                    // Progress를 사이클 길이에 고정해(계속 쌓이면 자리 났을 때 폭발적으로
                    // 밀어내므로) 다음 틱에 자리가 나는 즉시 배출하게 둔다.
                    if (!HasOutputSpace(processor, activeRecipe))
                    {
                        processor.Progress = activeRecipe.ProcessSeconds;
                        break;
                    }

                    processor.Progress -= activeRecipe.ProcessSeconds;
                    ProduceOutputs(processor, activeRecipe, statistics);
                    processor.IsProcessing = false;

                    if (processor.RecipeId < 0) break;
                    var nextRecipe = database.Recipes[processor.RecipeId];
                    if (!HasOutputSpace(processor, nextRecipe)) break;
                    if (!TryConsumeInputs(processor, nextRecipe, statistics)) break;
                    processor.ActiveRecipeId = processor.RecipeId;
                    processor.IsProcessing = true;
                }
            }
        }

        private static bool TryConsumeInputs(ProcessorInstance processor, in RecipeRuntime recipe,
            FactoryStatistics statistics)
        {
            var inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (processor.InputBuffer[inputs[i].ResourceId] < inputs[i].Amount) return false;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                processor.InputBuffer[inputs[i].ResourceId] -= inputs[i].Amount;
                statistics?.RecordConsumed(inputs[i].ResourceId, inputs[i].Amount);
            }
            return true;
        }

        // 이 레시피의 산출물을 전부 담을 자리가 OutputBuffer에 있는지. 하나라도 Capacity를
        // 넘기면 false — 그 사이클은 시작하지 않거나(입력 보존), 완성품을 들고 대기한다.
        private static bool HasOutputSpace(ProcessorInstance processor, in RecipeRuntime recipe)
        {
            var outputs = recipe.Outputs;
            for (int i = 0; i < outputs.Length; i++)
            {
                if (processor.OutputBuffer[outputs[i].ResourceId] + outputs[i].Amount > processor.Capacity)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ProduceOutputs(ProcessorInstance processor, in RecipeRuntime recipe,
            FactoryStatistics statistics)
        {
            var outputs = recipe.Outputs;
            for (int i = 0; i < outputs.Length; i++)
            {
                int resourceId = outputs[i].ResourceId;
                int amount = outputs[i].Amount;
                processor.OutputBuffer[resourceId] = System.Math.Min(
                    processor.OutputBuffer[resourceId] + amount,
                    processor.Capacity);
                statistics?.RecordProduced(resourceId, amount);
            }
        }
    }
}
