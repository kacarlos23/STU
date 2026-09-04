namespace STU.Domain.Operations;

public enum OperationJobKind { PropertyExport, PropertyImport }
public enum OperationJobStatus { Pending, Processing, AwaitingApproval, Completed, Failed }
public enum OperationFileFormat { Csv, GeoJson, Kml, GeoPackage }
public enum NotificationKind { Information, Assignment, OperationCompleted, OperationFailed }
