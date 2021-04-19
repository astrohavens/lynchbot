﻿using System;
using System.IO;
using System.Collections.Generic;

using Markov;

namespace lynchbot
{
    static class Program
    {
        public static Stream awsLambdaHandler(Stream inputStream)
        {
            Console.WriteLine("starting via lambda");
            Main(new string[0]);
            return inputStream;
        }

        public class CustomJsonLanguageConverter : Tweetinvi.Logic.JsonConverters.JsonLanguageConverter
        {
            public override object ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
            {
                return reader.Value != null
                    ? base.ReadJson(reader, objectType, existingValue, serializer)
                    : Tweetinvi.Models.Language.English;
            }
        }

        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.WriteLine("Beginning program");

            if (args.Length != 0 && args[0] == "sample-output")
            {
                SampleOutput();
            }
            else if (args.Length != 0 && args[0] == "evaluate-corpus-and-orders")
            {
                EvaluateCorpusAndOrders();
            }
            else
            {
                GenerateQuoteAndTweet();
            }
        }

        static void SampleOutput()
        {
            string[] quotes = GetQuotes();

            for (int i = 0; i < 20; i++)
            {
                string quote = GenerateQuoteWithBackoff();
                bool isCopy = false;
                foreach (string inputQuote in quotes)
                {
                    if (inputQuote.Contains(quote))
                    {
                        isCopy = true;
                        break;
                    }
                }
                Console.WriteLine(quote + " Is Copy: " + isCopy);
            }
        }

        static void EvaluateCorpusAndOrders()
        {
            EvaluateCorpus();
            EvaluateOrders();
        }

        static void GenerateQuoteAndTweet()
        {
            InitializeTwitterCredentials();

            Tweetinvi.Logic.JsonConverters.JsonPropertyConverterRepository.JsonConverters.Remove(typeof(Tweetinvi.Models.Language));
            Tweetinvi.Logic.JsonConverters.JsonPropertyConverterRepository.JsonConverters.Add(typeof(Tweetinvi.Models.Language), new CustomJsonLanguageConverter());

            string quote = GenerateQuoteWithBackoff();

            TweetQuote(quote);
        }

        static void InitializeTwitterCredentials()
        {
            string consumerKey = System.Environment.GetEnvironmentVariable("twitterConsumerKey");
            string consumerSecret = System.Environment.GetEnvironmentVariable("twitterConsumerSecret");
            string accessToken = System.Environment.GetEnvironmentVariable("twitterAccessToken");
            string accessTokenSecret = System.Environment.GetEnvironmentVariable("twitterAccessTokenSecret");

            if (consumerKey == null)
            {
                using (StreamReader fs = File.OpenText("localconfig/twitterKeys.txt"))
                {
                    consumerKey = fs.ReadLine();
                    consumerSecret = fs.ReadLine();
                    accessToken = fs.ReadLine();
                    accessTokenSecret = fs.ReadLine();
                }
            }

            Tweetinvi.Auth.SetUserCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret);
        }

        static void TweetQuote(string quote)
        {
            Console.WriteLine("Publishing tweet: " + quote);
            var tweet = Tweetinvi.Tweet.PublishTweet(quote);
        }

        static string GenerateQuote()
        {
            Random rand = new Random();

            int order = 1;
            if (rand.NextDouble() > 0.5)
            {
                // Add a small chance of a higher order chain. The higher order chains
                // produce output that is a lot closer to the source text. Too close
                // to have on all the time.
                order = 2;
            }

            Console.WriteLine("order " + order);

            MarkovChain<string> chain = GetChain(order);

            string generatedQuote = string.Join(" ", chain.Chain(rand));

            if (generatedQuote.Length > 280)
            {
                return GenerateQuote();
            }
            else 
            {
                return generatedQuote;
            }
        }

        static string GenerateQuoteWithBackoff()
        {
            Random rand = new Random();

            // Order 3 is the highest order that actually makes a meaningful difference.
            // Targeting 2 next states seems like it hits a balance of high and low orders.
            MarkovChainWithBackoff<string> chain = GetChainWithBackoff(3, 2);

            IEnumerable<string> result = chain.Chain(rand);
            string generatedQuote = string.Join(" ", result);

             if (generatedQuote.Length > 280)
            {
                return GenerateQuote();
            }
            else 
            {
                return generatedQuote;
            }
}

        static string TruncateQuote(string quote)
        {
            // Truncate long quotes to one sentence
            if (quote.Length < 140)
            {
                return quote;
            }

            char[] sentenceEnders = new char[] { '.', '!', '?' };

            int earliestSentenceEnderIndex = Int32.MaxValue;
            foreach (char ender in sentenceEnders)
            {
                int enderIndex = quote.IndexOf(ender);
                if (enderIndex > 0 && enderIndex < earliestSentenceEnderIndex)
                {
                    earliestSentenceEnderIndex = enderIndex;
                }
            }
            Console.WriteLine("truncating quote. Original: " + quote);

            return quote.Substring(0, earliestSentenceEnderIndex + 1);
        }

        static MarkovChain<string> GetChain(int order)
        {
            MarkovChain<string> chain = new MarkovChain<string>(order);

            foreach (string sourceQuote in GetQuotes())
            {
                string[] words = sourceQuote.Split(' ');
                chain.Add(words);
            }

            return chain;
        }

        static MarkovChainWithBackoff<string> GetChainWithBackoff(int maxOrder, int desiredNumNextStates)
        {
            MarkovChainWithBackoff<string> chain = new MarkovChainWithBackoff<string>(maxOrder, desiredNumNextStates);

            foreach (string sourceQuote in GetQuotes())
            {
                string[] words = sourceQuote.Split(' ');
                chain.Add(words);
            }

            return chain;
        }

        // We want the average options per state to be between 1.5 to 2.0 ideally.
        // If it's less than that then we're likely directly quoting the input text.
        static void EvaluateOrders()
        {
            for (int order = 1; order < 5; order++)
            {
                MarkovChain<string> chain = GetChain(order);

                var states = chain.GetStates();
                int numStates = 0;
                int numOptions = 0;
                foreach (ChainState<string> state in states)
                {
                    var nextStates = chain.GetNextStates(state);
                    int terminalWeight = chain.GetTerminalWeight(state);
                    numStates++;
                    numOptions += (nextStates != null ? nextStates.Count : 0);
                    numOptions += (terminalWeight > 0 ? 1 : 0); // If this is a possible termination of the chain, that's one option
                }

                Console.WriteLine("Order: " + order + " NumStates: " + numStates + " NumOptionsTotal: " + numOptions + " AvgOptionsPerState " + ((float)numOptions / (float)numStates));
            }
        }

        static void EvaluateCorpus()
        {
            string[] quotes = GetQuotes();
            Console.WriteLine("NumLines: " + quotes.Length);

            HashSet<string> uniqueWords = new HashSet<string>();
            int numTotalWords = 0;

            foreach (string line in quotes)
            {
                string[] words = line.Split(' ');

                numTotalWords += words.Length;
                uniqueWords.UnionWith(words);

                foreach (string word in words)
                {
                    if (word == null || word == "")
                    {
                        throw new Exception("Invalid string in corpus.");
                    }
                }
            }

            Console.WriteLine("NumTotalWords: " + numTotalWords);
            Console.WriteLine("NumUniqueWords: " + uniqueWords.Count);
        }

        public static string[] GetQuotes()
        {
            return new string[] {
"I know people will read the book for clues.", 
"It's real, real weird." , 
"It's the strangest thing to see how a life comes together from these different perspectives." , 
"I like to tell stories." , 
"I don't like to talk about my work." , 
"There's no reason for it." , 
"It's interesting to go back over your life." ,
"There are over 7.5 billion people on Earth and every life is different." , 
"You pop out of your mother and your life starts." , 
"So many things happen in the first hour!" , 
"And it's like, how come somebody becomes a scientist?" , 
"Certain gates are opened for them." , 
"Life doesn't have to happen any one way." , 
"It made me so unbelievably lucky." , 
"I might have wanted to shut the door and not leave a squeak of light coming through." , 
"I might have done that, but it doesn't sound like me." , 
"There are probably thousands and thousands of examples of people who had screwed-up lives and didn’t do the best things but did great work." , 
"You feel terrible for anyone who's been a victim." , 
"This subject is tricky business." , 
"Transcendental Meditation is the only thing I know of that over time would change people for the good on the deepest level." , 
"And political correctness came in." , 
"I love an idea for itself, and I love what cinema can do with some ideas." ,
"The ideas pass through the machine of me and get realized in a certain way." , 
"They're real." , 
"Theoreticaly." , 
"No." , 
"He seemed very intelligent." , 
"I had in my pocket $5,000 in cash that I’d saved from my per diems." , 
"My thinking was that I’d double it at least, so I put $5,000 into Chewy Nougats." , 
"I followed it for a while and the Chewy part left and it was just called Nougats." ,
"Then the Nou was gone and it was called Gats." , 
"Then it all disappeared." , 
"I don't even know what it was." , 
"I think it was candy." , 
"I never really knew." , 
"I've been bitten twice like that." , 
"I took it on faith." , 
"So, every human being has consciousness, but not every human being has the same amount." , 
"The potential is for infinite consciousness, enlightenment, total fulfillment, total liberation, immortality, no more dying, no more suffering." ,
"But enlightenment needs unfolding." , 
"An enlightened human being would be flowing with what we call natural law." , 
"I was in Italy, and this nun was asking me about Transcendental Meditation for children and wanted to know at what age kids were old enough to start." ,
"The nun freaked out about that." , 
"Maybe that tells you something about nuns." ,
"I'm not the greatest parent." , 
"I was on a Little League baseball team." ,
"You’ve got to find your way into a painting and that takes time and interruptions kill you." ,
"They make you crazy." ,
"They're a nightmare." , 
"If you’re full, that’s good or If you enjoy it, it’s good." ,
"Well, the dumpster had a metal ladder on it and I got into the dumpster and found the carton that the milkshakes came from." , 
"I had this onion dip and these vegetable chips." , 
"I'm trying to find those chips." ,
"Then for lunch I have one piece of bread, toasted, with some mayonnaise and some chicken." ,
"And coffees; a lot of coffees." , 
"On the weekend I have crunchy peanut butter and banana." ,
"I watch crime shows at night." , 
"Or shows about customizing cars." , 
"These electric toilets are so beautiful." , 
"It's got this great lavender-blue lamplight." , 
"People are detectives and we have life to find clues about in the same way we would with a piece of cinema." , 
"I've never missed a meditation." , 
"It's about 18 hours long." , 
"There's the cart and then there's the horse." ,
"They say that's like a psychogenic fugue." , 
"I'm pretty much a spring chicken." , 
"Peace comes from the unified field." ,
"I smoke cigarettes and it's an unfriendly world for smokers." ,
"I don't like the vaping." , 
"I hate the smell of marijuana." , 
"I suffer a hair of agoraphobia." , 
"That's the hole and not the doughnut." , 
"Happiness is not a new car." , 
"It brings in the gold and allows you to say good-bye to the garbage." , 
"It was just like rolling off a log." , 
"It's hard to stay on a log." , 
"I love vertical-grain Douglas-fir plywood." , 
"Doing TV was like going from a mansion to a hut." , 
"The denizens of the deep and all that." , 
"If you're going into the netherworld, you don't want to go in." , 
"It's such a sadness that you think you've seen a film on your fucking telephone." , 
"Perplexing." , 
"I was painting very dark paintings." , 
"I saw some little part of this figure moving." , 
"I built this one minute film on a sculptured screen with the sound of a siren going for an experimental painting and sculpture contest at the end of the year." , 
"It was actually untitled and I've since then titled it." , 
"Well, no they don't blow up." , 
"There's some fire involved and then they become sick." , 
"I saw a woman in a backyard squawking like a chicken." , 
"Crawling on her hands and knees in tall, dry grass." , 
"I saw many strange things." , 
"Yeah some things critics say bother me." , 
"Maybe in some places, it may slip towards black and white." , 
"I might want to desaturate the color." , 
"I love industry." , 
"I love fluid and smoke." , 
"I love the machines that make the things." , 
"I like to see sludge and man-made waste." , 
"I like to see what nature does to it, and to see man-made things juxtaposed with nature." , 
"The things you lose are the key to everything." , 
"I went off more into dreams and strange things." , 
"Well, if you set a piece of steel out in a vacant lot." , 
"The steel could be kind of nice." , 
"Then, nature starts going to work on it." , 
"Every time I hear sounds, I see pictures." , 
"I'm a shed builder." , 
"If I was just left alone, I would build sheds." , 
"I would become very excited with these coffees and a chocolate shake." , 
"We're super-special beings!" , 
"The big treasury within every human being." , 
"Today is a torment." , 
"I had to get dressed up." , 
"I'm a regular person. I do regular things." , 
"It's just interviews but every day is exciting." , 
"Night time dreams are not so important for feeling in my work." , 
"I like to just sit in a chair and daydream." , 
"I like to daydream." , 
"The world is getting louder every year." ,
"To sit and dream is a beautiful thing." , 
"I love highways, I love driving and the freedom of the open road." , 
"Moving from one place to another." , 
"You should enjoy the doing of a thing." , 
"All the tracks have to be mixed and mastered in a way that they sound like at least something on a computer." , 
"It's the sickest, most corrupt, decaying, fear-ridden city imaginable." , 
"The bags had a big zipper, and they’d open the zipper and shoot water into the bags with big hoses." , 
"With the zipper open and the bags sagging on the pegs, it looked like these big smiles." , 
"I called them the smiling bags of death." , 
"It’s like you miss jabbing your knee with an icepick!" , 
"Which had pitch oozing out of it." , 
"No I haven't changed my mind." , 
"And if I don't like what comes out, I change the words and then a new things comes out," , 
"And if that's not quite right I change the words, change the words, change the words." ,
"It's a beautiful day when that happens." , 
"And the idea tells you everything." , 
"It’s all baloney." , 
"A scene, separate, you know, by itself for the Internet or whatever." , 
"Ideas as like fish." , 
"You don’t make the fish, you catch the fish." , 
"Any kind of suffering cramps the flow of creativity." , 
"Let’s say that Van Gogh, every time he went out and painted, he got diarrhea." , 
"In fact, going into the subway, I felt I was really going down into hell." , 
"But I longed for some sort of — not a catastrophe but something out of the ordinary to happen." , 
"No, I had a tremendous smile." , 
"It was like the Cadillac of perambulators that we got at Goodwill for about a buck." , 
"Whenever you finish something, it starts decaying." , 
"Well, right above my ears there are these kind of silver — these fish-scale silver hairs." , 
"Sex was like a dream." , 
"And it kept on going, because I see the vast realm of sex." , 
"That is has all these different levels, from lust and fearful, violent sex to the real spiritual thing at the other end." , 
"And if you’re able to deal with a full deck, I think then you’d be pretty trustworthy." , 
"when you see close-ups of the eruption, the smoke.",
"The curls of the smoke are like the curls of the smoke in an atomic bomb.",
"It's just a slow motion version of an explosion, sort of.",
"Lit at night from a lightbulb and red lips and the color blue.",
"The ear is like a canal.",
"It's like an opening, a little egress into another place.",
"I don't have any idea, but it's not fifty-two.",
"It takes two to tango, and this is what happened to me.",
"Sometimes a jolt of electricity at a certain point of your life is helpful.",
"The morgue was kind of a clinical thing.",
"Their dog, they fed it so much, it looked like a water balloon with little legs.",
"It was like a Chihuahua with a watermelon in the middle.",
"Like the duck.",
"The duck is real good for many things.",
"If you could interpret a duck, if you could work with the rules of a duck.",
"The idea of birth was a mysterious and fantastic thing, involving, again, like sex, just pure meat and blood and hair.",
"You betcha!",
"He's either Daddy or he's Baby.",
"Just like you talk about a piece of decaying meat.",
"Is what does the angriest dog.",
"The decay I feel is, like, spreading faster than the building.",
"Just the words dark secret are so beautiful.",
"This woman was having this operation and asked the doctor to save it for me.",
"I'm not a great flier.",
"We should be like little puppy dogs.",
"Bliss is our nature.",
"That's an awkward question and I'm not gonna answer it.",
"Would you like to go home?",
"I'm not really a film buff, or an art buff, or know about history books.",
"She was just caught one night in a fever.",
"Repeating the alphabet in her sleep.",
"Mine just came out from a dream.",
"No, I never followed much of avant-garde cinema at the time.",
"Yes, then I soon began making lots of drawings on matchboxes.",
"I was smoking at the time.",
"I just tried to make the prints look like those Post-it drawings.",
"Some elements like a head, hand, leg or whatever gets made in clay.",
"In Vedic science, everything comes from zero, everything.",
"But seven is a cosmic number, which I like.",
"This rock with seven eyes had a meaning to me, and so that’s what I did.",
"Ochre, I love, love yellow ochre.",
"Like I’d fallen in love with cardboard as a surface to paint on.",
"The idea of triptychs is magical, and so each panel is somewhat different in my case.",
"I always liked Christmas tree bulbs, and I love electricity, making lamps.",
"I rely on the outside air and breeze because a lot of times I’m working with toxic materials so it works out to have this outside painting studio.",
"I know that every human being has a treasury within.",
"It’s like a butterfly net. You get a butterfly net.",
"You swing it around, and to catch some butterflies as ideas.",
"Romance is a big deal.",
"He was like a brother in sound.",
"We would hit things and drag the microphone and just see what would happen.",
"It had a distant, haunting quality.",
"I felt this lady lived in the radiator.",
"There's a bar, and huge, giant, colossal factories.",
"Huge smokestacks, building smoke, thick atmospheres.",
"I noticed I did have an anger.",
"It was driving me crazy, because you hear we only use five or ten percent of our brains.",
"Stories need to have dark and light, turmoil, all those things.",
"Stories should have the suffering, not the people.",
"I always say ideas drive the boat",
"There are no rules.", 
"Whereas others really light my fire.",
"Life is a 24/7 movie.",
"Television was a joke when we made that show.", 
"These guys aren’t just a bunch of goofballs.",
"I felt the suffocating rubber clown suit of negativity dissolving.",
"You say goodbye to the garbage and infusing gold.",
"It’s a field that is so beautiful, so powerful, it’s eternal, it’s immortal, it’s immutable, it’s infinite, it’s unbounded.",
"You wanna buy a bunch of people coffees.",
"The torment is inside the people.",
"Au contraire, you get more of you!",
"We start transcending that conduit widens out and you start enjoying things and love the doing.",
"I once noticed an article about somebody here in Hollywood who ran his whole business on fear, like it was a macho, cool thing.",
"And since I wanted movement I experimented until I found a type of movement that gave me the effect you see in the photograph.",
"It would be a forest, because I love wood.",
"There’s a beautiful mystery in a forest particularly a forest of Ponderosa Pine.",
"It has an incredible smell and a beautiful look, and the trees are not close together.",
"The forest is very friendly with chipmunks and birds and deer and babbling brooks with ice-cold, crystal clear water.",
"The forest floor is deep with pine needles, so it is very soft.",
"And the sunlight is so beautiful as it filters through, thrilling the soul.",
"There is a silence in the forest.", 
"Meditation gives an experience much sweeter than this donut.",
"It gives the experience of the sweetest nectar of life: pure bliss consciousness.",
"Transcendental Meditation is the vehicle that takes you there, but it’s the experience that does everything.",
"The beauty of Transcendental Meditation is that it gives effortless transcending.",
"It’s like the X-ray machine.",
"You can say it till the cows come home: it can't see my bones.",
"You step in front of the X-ray machine, there your bones are.",
"I hate speaking in public.", 
"Wow, that's wild.",
"I always say it's my most spiritual movie.",
"That ocean of pure consciousness is also known by modern science as the 'Unified Field.'",
"This light is a certain kind of thing.",
"You get a feeling in California that you can do what you think to do.",
"I grew up in the Northwest, and if you couldn’t see it, feel it, touch it or kick it then it didn’t exist.",
"And when you grow up then you start getting anxieties, you start getting fears; things happen and you start getting angry, you get confused.",
"And, you know, meditation throws stress off kids like water off a duck’s back.",
"I don’t remember them ever coming to my house, but I was raised Presbyterian.",
"I feel some of the beautiful original keys from the Master have been lost.",
"We’re sparks off the Divine Flame.",
"It’s a field of pure knowingness.",
"You get an idea and that is like a supreme gift.",
"It’s like the chef catches this fantastic fish, and now he can go about cooking it in a special way, using his talents for cooking the fish.",
"It has something to do with the birth of rock'n'roll.",
"I used to have one recurring dream where I am in the desert.",
"Only when he gets very close I know it is my bad father.",
"I get way up on the roof and I can hear his footsteps below and he can't find me.",
"It is like staying true to the fundamental notes of a chord.",
"Get them correct and the harmonics will follow.",
"You look at the image and it's not all there.",
"And I've been meditating twice a day for 41 years now, never missed a meditation in those 41 years.",
"It's like trying to help a sick tree at the level of the leaves.",
"It's not a macho thing, maybe they think, or it's not an American thing.",
"It's such a powerful and beautiful thing to experience the treasury within.",
"This is called supreme enlightenment, and it just needs unfolding by transcending each day.",
"The human being is like a light bulb.",
"It's not really romantic for the artist to be starving, cold and suffering in the garret.",
"Like I said before, the peace-creating groups are so important for raising the collective consciousness and bringing real peace.",
"It is the most profound, most beautiful, eternal field and it's all-positive.",
"Unless one is supremely enlightened, Transcendental Meditation is something to be seriously considered for a better and better life.",
"We would have heaven on earth, peace on earth.",
"Anyone who experiences that kingdom of heaven within infuses some of that every time they transcend.",
"Well, we all have our blessings and our curses.",
"I’ve got a love affair with music.",
"I hung out with a cow.",
"Bullshit.",
"Who gives a fucking shit how long a scene is.",
"I love to eat cheese.",
"And I would write on the napkins. It was like having a desk.",
"And you need paper?",
"Is there a segue from spaghetti to tuna fish?",
"Then he took me for a ride in his Ferrari for a lunch.",
"And I continue to eat cheese.",
"One lots of times gets bad ideas.",
"I am going to put these panties in my mouth.",
"I believe I looked at semen.",
"Suffering is really not a part of the thing.",
"When you're on a roll, you're high.",
"And lithographs is one medium, and watercolors is another.",
"I like childlike painting.",
"I like painting, and I think of it as totally organic, like mud and water and organic phenomenon.",
"I just love the world of paint.",
"I like sometimes to cut a hole in the painting.",
"I like sometimes for the paint to come out, like sculpture.",
"So the idea drives the boat.",
"I haven't really gotten into sewing.",
"I would like to sew.", 
"I got a new sewing machine, but I havne't learned how to use it yet.",
"This is a breakthrough for people to stop suffering.",
"A little photoshop on my computer.",
"A lot of people suffer in isolation.",
"I’ve got my YouTube channel, and I do a weather report every morning.",
"I'm lucky I can enjoy isolation.",
"I'm just a regular human being like everyboyd else.",
"What I really like is to be at home, working.",
"I can't live without coffee, transcendental meditation, American Spirit cigarettes.",
"The most delicious food is far and away super-crisp, almost snapping-crisp bacon with two scrambled eggs, toasted hash browns, white toast with butter and jam, and coffee.",
"I have deep love for my Swatch watch.",
"Maybe if you sat on a bed of nails to do it…no, not so much comfort.",
"I don't paint the town red.",
"But when I do go out, people always want to touch my hair.",
"I first started buttoning my shirt because, for some reason, my collarbone is very sensitive.",
"And I don't like to feel wind on my collarbone.",
"The best cities of all are Los Angeles and Paris.",
"There was one where I'd release the paper, which would soar with the speed of the car and slam into the front door of this building, triggering its lobby lights.",
"My advice to finger-painters would be to go with your intuition.",
"A brush will do a certain thing…but your finger will do a different thing.",
"I recently collected a toy telephone.",
"It's from the 1940s and made of metal.",
"And a good movie idea is often like a girl you're in love with.",
"But you know she's not the kind of girl you bring home to your parents.",
"Because sometimes they hold some dark and troubling things.",
"But I'm not a businessman and I think my line of coffee will die the death this year.",




             };
        }
    }
}
