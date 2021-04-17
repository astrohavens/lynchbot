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
            else if (args.Length != 0 && args[0] == "evalutate-corpus-and-orders")
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
"And if that's not quite right I change the words, change the words, change the words." ,             };
        }
    }
}
