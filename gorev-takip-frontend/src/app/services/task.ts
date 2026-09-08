import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TaskItem {
  id: number;
  title: string;
  description: string;
  status: string;
  priority: string;
  dueDate: string | null;
  projectId: number;
  assignedToId?: number | null;

  // Backend artık görevleri entity yerine projeksiyon olarak dönüyor; bu iki alan da
  // yanıtta geliyor. Sayesinde pano kartında projeyi ve atanan kişiyi göstermek için
  // ayrı istek atmaya gerek kalmıyor.
  projectName?: string | null;
  assignedToFullName?: string | null;

  /** Sütun içindeki sırası. Küçük değer üstte. */
  sortOrder?: number;
  /** "Tamamlandı"ya geçtiği an; eski tamamlanmış görevleri gizlemek için kullanılıyor. */
  completedAt?: string | null;

  // Kartta rozet olarak gösterilen sayılar - ayrı istek atmadan gelsin diye
  // görev yanıtının içinde dönüyorlar.
  commentCount?: number;
  attachmentCount?: number;
  checklistTotal?: number;
  checklistDone?: number;
}

export interface ChecklistItem {
  id: number;
  text: string;
  isDone: boolean;
  sortOrder: number;
}

export interface TaskAttachment {
  id: number;
  fileName: string;
  url: string;
  contentType: string;
  sizeBytes: number;
  userId: number | null;
  userFullName: string | null;
  createdAt: string;
}

export interface TaskActivityItem {
  id: number;
  activityType: string;
  detail: string;
  userId: number | null;
  userFullName: string | null;
  userAvatarUrl: string | null;
  createdAt: string;
}

/** Görev detay sayfasının tek istekte aldığı paket. */
export interface TaskDetail {
  task: TaskItem;
  checklist: ChecklistItem[];
  attachments: TaskAttachment[];
}

export interface TaskPayload {
  id: number;
  title: string;
  description: string;
  status: string;
  priority: string;
  dueDate: string | null;
  projectId: number;
  assignedToId: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class TaskService {
  private apiUrl = 'https://localhost:7236/api/tasks';

  constructor(private http: HttpClient) { }

  /**
   * @param includeArchived true ise 30 günden eski tamamlanmış görevler de gelir.
   * Varsayılan olarak gizleniyorlar; aksi halde "Tamamlandı" sütunu sonsuza kadar büyüyor.
   */
  getTasks(includeArchived = false): Observable<TaskItem[]> {
    const query = includeArchived ? '?includeArchived=true' : '';
    return this.http.get<TaskItem[]>(`${this.apiUrl}${query}`);
  }

  getTaskDetail(id: number): Observable<TaskDetail> {
    return this.http.get<TaskDetail>(`${this.apiUrl}/${id}`);
  }

  getActivity(id: number): Observable<TaskActivityItem[]> {
    return this.http.get<TaskActivityItem[]>(`${this.apiUrl}/${id}/activity`);
  }

  /**
   * Bir sütunun tamamının yeni sırasını kaydeder. Sürükle-bırak sonrası hedef sütundaki
   * görev kimlikleri sırayla gönderiliyor; sütunlar arası taşımada durum da burada
   * güncelleniyor. Yalnızca yöneticiler kullanabiliyor - sıralama başkalarının
   * kartlarının da yerini değiştirdiği için.
   */
  reorder(status: string, orderedIds: number[]): Observable<void> {
    return this.http.patch<void>(`${this.apiUrl}/reorder`, { status, orderedIds });
  }

  createTask(task: Omit<TaskPayload, 'id'>): Observable<TaskItem> {
    return this.http.post<TaskItem>(this.apiUrl, task);
  }

  updateTask(id: number, task: TaskPayload): Observable<void> {
    return this.http.put<void>(`${this.apiUrl}/${id}`, task);
  }

  /**
   * Sadece görevin durumunu değiştirir. Panoda kart sürüklendiğinde ve görev detayında
   * ekip üyesi durumu değiştirdiğinde bu kullanılıyor: tam nesneyi PUT etmek, "ekip üyesi
   * yalnızca durumu değiştirebilir" kuralına takılıp 403 dönmesine yol açıyordu.
   */
  updateTaskStatus(id: number, status: string): Observable<void> {
    return this.http.patch<void>(`${this.apiUrl}/${id}/status`, { status });
  }

  deleteTask(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
