using System;
using System.Linq;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using Libplanet.Types.Assets;
using Nekoyume.Action.Exceptions.AdventureBoss;
using Nekoyume.Helper;
using Nekoyume.Model.AdventureBoss;
using Nekoyume.Model.State;
using Nekoyume.Module;
using Nekoyume.TableData;
using Nekoyume.TableData.AdventureBoss;

namespace Nekoyume.Action.AdventureBoss
{
    [Serializable]
    [ActionType(TypeIdentifier)]
    public class Wanted : ActionBase
    {
        public const string TypeIdentifier = "wanted";
        public const int RequiredStakingLevel = 5;
        public const int MinBounty = 100;

        public int Season;
        public FungibleAssetValue Bounty;
        public Address AvatarAddress;

        public override IValue PlainValue =>
            Dictionary.Empty
                .Add("type_id", TypeIdentifier)
                .Add("values", List.Empty
                    .Add(Season.Serialize())
                    .Add(Bounty.Serialize())
                    .Add(AvatarAddress.Serialize()));

        public override void LoadPlainValue(IValue plainValue)
        {
            var list = (List)((Dictionary)plainValue)["values"];
            Season = list[0].ToInteger();
            Bounty = list[1].ToFungibleAssetValue();
            AvatarAddress = list[2].ToAddress();
        }

        public override IWorld Execute(IActionContext context)
        {
            context.UseGas(1);
            var states = context.PreviousState;
            var currency = states.GetGoldCurrency();

            var latestSeason = states.GetLatestAdventureBossSeason();

            // Validation
            if (!Bounty.Currency.Equals(currency))
            {
                throw new InvalidCurrencyException("");
            }

            if (Bounty < MinBounty * currency)
            {
                throw new InvalidBountyException(
                    $"Given bounty {Bounty.MajorUnit}.{Bounty.MinorUnit} is less than {MinBounty}");
            }

            var balance = states.GetBalance(context.Signer, currency);
            if (balance < Bounty)
            {
                throw new InsufficientBalanceException($"{Bounty}", context.Signer, balance);
            }

            if (Season <= 0 ||
                Season > latestSeason.Season + 1 || Season < latestSeason.Season ||
                (Season == latestSeason.Season &&
                 context.BlockIndex > latestSeason.EndBlockIndex) ||
                (Season == latestSeason.Season + 1 &&
                 context.BlockIndex < latestSeason.NextStartBlockIndex)
               )
            {
                throw new InvalidAdventureBossSeasonException(
                    $"Given season {Season} is not valid season."
                );
            }

            // Cannot put bounty in two seasons in a row
            if (Season > 1)
            {
                var prevBountyBoard = states.GetBountyBoard(Season - 1);
                if (prevBountyBoard.Investors.Select(i => i.AvatarAddress).Contains(AvatarAddress))
                {
                    throw new PreviousBountyException(
                        "You've put bounty in previous season. Cannot put bounty two seasons in a row"
                    );
                }
            }

            if (!Addresses.CheckAvatarAddrIsContainedInAgent(context.Signer, AvatarAddress))
            {
                throw new InvalidAddressException();
            }

            var requiredStakingAmount = states.GetSheet<MonsterCollectionSheet>()
                .OrderedList.First(row => row.Level == RequiredStakingLevel).RequiredGold;
            var stakedAmount =
                states.GetStakedAmount(states.GetAvatarState(AvatarAddress).agentAddress);
            if (stakedAmount < requiredStakingAmount * currency)
            {
                throw new InsufficientStakingException(
                    $"Current staking {stakedAmount.MajorUnit} is not enough: requires {requiredStakingAmount}"
                );
            }

            BountyBoard bountyBoard;
            // Create new season if required
            if (latestSeason.Season == 0 ||
                latestSeason.NextStartBlockIndex <= context.BlockIndex)
            {
                var seasonInfo = new SeasonInfo(Season, context.BlockIndex);
                bountyBoard = new BountyBoard(Season);
                var exploreBoard = new ExploreBoard(Season);

                // Set season info: boss and reward
                var random = context.GetRandom();
                var adventureBossSheet = states.GetSheet<AdventureBossSheet>();
                var boss = adventureBossSheet.OrderedList[
                    random.Next(0, adventureBossSheet.Values.Count)
                ];
                seasonInfo.BossId = boss.BossId;

                var wantedReward = states.GetSheet<AdventureBossWantedRewardSheet>()
                    .OrderedList.First(row => row.AdventureBossId == boss.Id);
                bountyBoard.SetReward(wantedReward, random);

                var contribReward = states.GetSheet<AdventureBossContributionRewardSheet>()
                    .OrderedList.First(row => row.AdventureBossId == boss.Id);
                exploreBoard.SetReward(contribReward, random);

                states = states.SetSeasonInfo(seasonInfo);
                states = states.SetLatestAdventureBossSeason(seasonInfo);
                states = states.SetBountyBoard(Season, bountyBoard);
                states = states.SetExploreBoard(Season, exploreBoard);
            }

            // Just update bounty board
            else
            {
                bountyBoard = states.GetBountyBoard(Season);
            }

            // FIXME: Send bounty to seasonal board
            states = states.TransferAsset(context, context.Signer,
                Addresses.BountyBoard.Derive(AdventureBossHelper.GetSeasonAsAddressForm(Season)),
                Bounty);
            bountyBoard.AddOrUpdate(AvatarAddress, states.GetAvatarState(AvatarAddress).name,
                Bounty);
            return states.SetBountyBoard(Season, bountyBoard);
        }
    }
}
