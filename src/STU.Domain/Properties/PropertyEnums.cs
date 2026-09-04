namespace STU.Domain.Properties;

public enum PropertyRegistrationStatus { Draft = 1, Active = 2 }
public enum PropertySituation { Occupied = 1, Vacant = 2, Abandoned = 3, Commercial = 4, Other = 5 }
public enum VisitType { Registration = 1, Routine = 2, FollowUp = 3, Attempt = 4 }
public enum VisitOutcome { Completed = 1, NoAnswer = 2, Refused = 3, AccessBlocked = 4, Rescheduled = 5 }
