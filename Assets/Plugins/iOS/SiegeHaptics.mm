// Core Haptics bridge. Patterns are authored in C# (Haptics.cs) and sent as parallel arrays;
// one dynamic continuous player drives the bow draw tension.
#import <CoreHaptics/CoreHaptics.h>
#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>

static CHHapticEngine *sEngine = nil;
static id<CHHapticAdvancedPatternPlayer> sContinuous = nil;
static BOOL sSupported = NO;

static int sPlays = 0;
static BOOL sRunning = NO;

static void StartEngine()
{
    if (sEngine == nil) return;
    NSError *err = nil;
    [sEngine startAndReturnError:&err];
    sRunning = (err == nil);
    if (err) NSLog(@"[SiegeHaptics] start failed: %@", err);
    else NSLog(@"[SiegeHaptics] engine started");
}

// The engine stops on audio-session interruptions / backgrounding; bring it back before any play.
static void EnsureRunning()
{
    if (!sRunning) StartEngine();
}

extern "C" {

// Playback category so game audio plays with the ringer/silent switch off, mixing with other apps.
// Also explicitly allow haptics while the AR camera is capturing.
void SiegeAudio_ConfigureSession()
{
    AVAudioSession *s = [AVAudioSession sharedInstance];
    NSError *err = nil;
    [s setCategory:AVAudioSessionCategoryPlayback mode:AVAudioSessionModeDefault
           options:AVAudioSessionCategoryOptionMixWithOthers error:&err];
    if (err) NSLog(@"[SiegeAudio] setCategory failed: %@", err);
    if (@available(iOS 13.0, *))
    {
        err = nil;
        [s setAllowHapticsAndSystemSoundsDuringRecording:YES error:&err];
        if (err) NSLog(@"[SiegeAudio] allowHaptics failed: %@", err);
    }
    err = nil;
    [s setActive:YES error:&err];
    if (err) NSLog(@"[SiegeAudio] setActive failed: %@", err);
    NSLog(@"[SiegeAudio] session category=%@", s.category);
}

bool SiegeHaptics_Init()
{
    if (@available(iOS 13.0, *))
    {
        sSupported = CHHapticEngine.capabilitiesForHardware.supportsHaptics;
        NSLog(@"[SiegeHaptics] supportsHaptics=%d", sSupported);
        if (!sSupported) return false;
        NSError *err = nil;
        sEngine = [[CHHapticEngine alloc] initWithAudioSession:nil error:&err];
        if (err != nil || sEngine == nil) { NSLog(@"[SiegeHaptics] engine init failed: %@", err); sSupported = NO; return false; }
        sEngine.playsHapticsOnly = YES;
        sEngine.autoShutdownEnabled = NO;
        sEngine.resetHandler = ^{ NSLog(@"[SiegeHaptics] engine reset"); sContinuous = nil; sRunning = NO; StartEngine(); };
        sEngine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
            NSLog(@"[SiegeHaptics] engine stopped reason=%ld", (long)reason);
            sContinuous = nil;
            sRunning = NO;
            dispatch_async(dispatch_get_main_queue(), ^{ StartEngine(); });
        };
        StartEngine();
        return true;
    }
    return false;
}

// kinds[i]: 0 = transient, 1 = continuous (uses durations[i]).
void SiegeHaptics_Play(int count, const float *times, const float *durations, const float *intensities,
                       const float *sharpnesses, const int *kinds, float decay)
{
    if (!sSupported || sEngine == nil || count <= 0) return;
    EnsureRunning();
    if (@available(iOS 13.0, *))
    {
        NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:count];
        for (int i = 0; i < count; i++)
        {
            NSMutableArray *params = [NSMutableArray arrayWithObjects:
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:intensities[i]],
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:sharpnesses[i]], nil];
            CHHapticEvent *e;
            if (kinds[i] == 0)
            {
                e = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient parameters:params relativeTime:times[i]];
            }
            else
            {
                if (decay > 0)
                {
                    [params addObject:[[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDDecayTime value:decay]];
                    [params addObject:[[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDSustained value:0]];
                }
                e = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous parameters:params
                                               relativeTime:times[i] duration:durations[i]];
            }
            [events addObject:e];
        }
        NSError *err = nil;
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&err];
        if (err != nil) { NSLog(@"[SiegeHaptics] pattern error: %@", err); return; }
        id<CHHapticPatternPlayer> player = [sEngine createPlayerWithPattern:pattern error:&err];
        if (err != nil) { NSLog(@"[SiegeHaptics] player error: %@", err); StartEngine(); return; }
        [player startAtTime:CHHapticTimeImmediate error:&err];
        if (err != nil) { NSLog(@"[SiegeHaptics] start error: %@ - restarting engine", err); StartEngine(); [player startAtTime:CHHapticTimeImmediate error:nil]; }
        else if (sPlays++ < 5) NSLog(@"[SiegeHaptics] played %d events", count);
    }
}

void SiegeHaptics_StartContinuous(float intensity, float sharpness)
{
    if (!sSupported || sEngine == nil) return;
    if (@available(iOS 13.0, *))
    {
        if (sContinuous != nil) return;
        EnsureRunning();
        NSError *err = nil;
        CHHapticEvent *e = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous parameters:@[
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity value:1],
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness value:0.5]]
            relativeTime:0 duration:30];
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[e] parameters:@[] error:&err];
        if (err != nil) return;
        sContinuous = [sEngine createAdvancedPlayerWithPattern:pattern error:&err];
        if (err != nil) { sContinuous = nil; return; }
        [sContinuous startAtTime:CHHapticTimeImmediate error:&err];
    }
}

void SiegeHaptics_UpdateContinuous(float intensity, float sharpness)
{
    if (sContinuous == nil) return;
    if (@available(iOS 13.0, *))
    {
        NSError *err = nil;
        CHHapticDynamicParameter *i = [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl value:intensity relativeTime:0];
        CHHapticDynamicParameter *s = [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticSharpnessControl value:sharpness - 0.5f relativeTime:0];
        [sContinuous sendParameters:@[i, s] atTime:CHHapticTimeImmediate error:&err];
    }
}

void SiegeHaptics_StopContinuous()
{
    if (sContinuous == nil) return;
    if (@available(iOS 13.0, *))
    {
        NSError *err = nil;
        [sContinuous stopAtTime:CHHapticTimeImmediate error:&err];
    }
    sContinuous = nil;
}

}
