#include <stdio.h>
#include <stdlib.h>
#include <pthread.h>
#include <unistd.h>

#define THRESHOLD_FACTOR 2 // Threshold = Total Processes / Stack Nodes

typedef enum {
    FOREGROUND,
    BACKGROUND
}
ProcessType;

typedef struct Process
{
    int id;
    ProcessType type;
    struct Process* next;
}
Process;

typedef struct StackNode
{
    Process* process_list;
    struct StackNode* next;
}
StackNode;

StackNode* stack = NULL;
pthread_mutex_t stackLock;

void push(StackNode** stack, Process* process)
{
    pthread_mutex_lock(&stackLock);

    if (*stack == NULL)
    {
        *stack = (StackNode*)malloc(sizeof(StackNode));
        (*stack)->process_list = process;
        (*stack)->next = NULL;
    }
    else
    {
        if (process->type == FOREGROUND)
        {
            process->next = (*stack)->process_list;
            (*stack)->process_list = process;
        }
        else
        {
            Process* current = (*stack)->process_list;
            if (current == NULL)
            {
                (*stack)->process_list = process;
            }
            else
            {
                while (current->next != NULL)
                {
                    current = current->next;
                }
                current->next = process;
                process->next = NULL;
            }
        }
    }

    pthread_mutex_unlock(&stackLock);
}

Process* pop(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    if (*stack == NULL || (*stack)->process_list == NULL)
    {
        pthread_mutex_unlock(&stackLock);
        return NULL;
    }

    StackNode* topNode = *stack;
    Process* process = topNode->process_list;
    topNode->process_list = process->next;
    if (topNode->process_list == NULL)
    {
        *stack = topNode->next;
        free(topNode);
    }

    pthread_mutex_unlock(&stackLock);
    return process;
}

void promote(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    if (*stack == NULL || (*stack)->next == NULL)
    {
        pthread_mutex_unlock(&stackLock);
        return;
    }

    StackNode* current = *stack;
    StackNode* previous = NULL;

    while (current->next != NULL)
    {
        previous = current;
        current = current->next;
    }

    if (current != NULL && previous != NULL && current->process_list != NULL)
    {
        Process* headProcess = current->process_list;
        current->process_list = headProcess->next;
        headProcess->next = previous->process_list;
        previous->process_list = headProcess;

        if (current->process_list == NULL)
        {
            previous->next = current->next;
            free(current);
        }
        else
        {
            StackNode* newNode = (StackNode*)malloc(sizeof(StackNode));
            newNode->process_list = headProcess;
            newNode->next = previous->next;
            previous->next = newNode;
        }
    }

    pthread_mutex_unlock(&stackLock);
}

void split_n_merge(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    StackNode* current = *stack;
    int totalProcesses = 0, stackNodes = 0;

    while (current != NULL)
    {
        stackNodes++;
        Process* process = current->process_list;
        while (process != NULL)
        {
            totalProcesses++;
            process = process->next;
        }
        current = current->next;
    }

    if (stackNodes == 0)
    {
        pthread_mutex_unlock(&stackLock);
        return;
    }

    int threshold = totalProcesses / (stackNodes * THRESHOLD_FACTOR);
    int split_occurred = 0;
    do
    {
        split_occurred = 0;
        current = *stack;

        while (current != NULL)
        {
            Process* process = current->process_list;
            int length = 0;

            while (process != NULL)
            {
                length++;
                process = process->next;
            }

            if (length > threshold)
            {
                Process* firstHalf = current->process_list;
                Process* secondHalf = current->process_list;
                Process* prev = NULL;
                for (int i = 0; i < length / 2; i++)
                {
                    prev = secondHalf;
                    secondHalf = secondHalf->next;
                }

                if (prev != NULL) prev->next = NULL;

                StackNode* newNode = (StackNode*)malloc(sizeof(StackNode));
                newNode->process_list = secondHalf;
                newNode->next = current->next;
                current->next = newNode;

                split_occurred = 1;
                break;
            }
            current = current->next;
        }
    } while (split_occurred);

    pthread_mutex_unlock(&stackLock);
}

void rotate(StackNode** stack)
{
    pthread_mutex_lock(&stackLock);
    if (*stack == NULL || (*stack)->next == NULL)
    {
        pthread_mutex_unlock(&stackLock);
        return;
    }

    StackNode* current = *stack;
    StackNode* previous = NULL;

    while (current->next != NULL)
    {
        previous = current;
        current = current->next;
    }

    if (previous != NULL)
    {
        previous->next = NULL;
        current->next = *stack;
        *stack = current;
    }

    pthread_mutex_unlock(&stackLock);
}

void printProcess(Process* p)
{
    if (p == NULL) return;

    printf("Process ID: %d ", p->id);
    if (p->type == FOREGROUND)
    {
        printf("Type: Foreground\n");
    }
    else
    {
        printf("Type: Background\n");
    }
}

int main()
{
    pthread_mutex_init(&stackLock, NULL);

    Process* process1 = (Process*)malloc(sizeof(Process));
    process1->id = 0;
    process1->type = FOREGROUND;
    process1->next = NULL;
    push(&stack, process1);

    Process* process2 = (Process*)malloc(sizeof(Process));
    process2->id = 1;
    process2->type = BACKGROUND;
    process2->next = NULL;
    push(&stack, process2);

    promote(&stack);
    split_n_merge(&stack);
    rotate(&stack); // Clock-wise 순환

    Process* p;
    while ((p = pop(&stack)) != NULL)
    {
        printProcess(p);
        free(p);
    }

    pthread_mutex_destroy(&stackLock);
    return 0;
}//2-1
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <pthread.h>

#define MAX_PROCESSES 100

typedef enum { RUNNING, READY, BLOCKED }
ProcessState;

typedef struct {
    int pid;
    char type; // 'F': FG, 'B': BG
    int remainingTime; // WQ에 있을 때 남은 시간
    char promoted; // 프로모션 여부 '*' or ' '
    ProcessState state;
}
Process;

Process dq[MAX_PROCESSES];
Process wq[MAX_PROCESSES];
int dq_count = 0;
int wq_count = 0;
int current_pid = 0;
pthread_mutex_t lock = PTHREAD_MUTEX_INITIALIZER;
int running = 1;

void signal_handler(int sig)
{
    if (sig == SIGINT)
    {
        printf("\nSIGINT received, shutting down...\n");
        running = 0;
    }
}

void add_to_dq(Process p)
{
    pthread_mutex_lock(&lock) ;
    if (dq_count < MAX_PROCESSES)
    {
        dq[dq_count++] = p;
    }
    else
    {
        printf("DQ Overflow\n");
    }
    pthread_mutex_unlock(&lock) ;
}

void add_to_wq(Process p)
{
    pthread_mutex_lock(&lock) ;
    wq[wq_count++] = p;
    // 남은 시간이 가까운 순서로 정렬
    for (int i = 0; i < wq_count - 1; i++)
    {
        for (int j = 0; j < wq_count - i - 1; j++)
        {
            if (wq[j].remainingTime > wq[j + 1].remainingTime)
            {
                Process temp = wq[j];
                wq[j] = wq[j + 1];
                wq[j + 1] = temp;
            }
        }
    }
    pthread_mutex_unlock(&lock) ;
}

void* shell_process(void* arg)
{
    char input[100];
    while (running)
    {
        printf("Shell: Enter a command: ");
        fgets(input, sizeof(input), stdin);
        input[strcspn(input, "\n")] = '\0'; // 개행 문자 제거

        if (strcmp(input, "exit") == 0)
        {
            break;
        }

        char** args = parse(input);
        exec(args);
    }
    return NULL;
}

void print_queues()
{
    pthread_mutex_lock(&lock) ;
    printf("DQ: ");
    for (int i = 0; i < dq_count; i++)
    {
        printf("%d%c%s(%s) ", dq[i].pid, dq[i].type, dq[i].promoted == '*' ? "*" : "",
               dq[i].state == RUNNING ? "RUNNING" :
               dq[i].state == READY ? "READY" : "BLOCKED");
    }
    printf("\nWQ: ");
    for (int i = 0; i < wq_count; i++)
    {
        printf("%d%c:%d(%s) ", wq[i].pid, wq[i].type, wq[i].remainingTime,
               wq[i].state == READY ? "READY" : "BLOCKED");
    }
    printf("\n");
    pthread_mutex_unlock(&lock) ;
}

void wake_up_processes()
{
    pthread_mutex_lock(&lock) ;
    int i = 0;
    while (i < wq_count)
    {
        wq[i].remainingTime--;
        if (wq[i].remainingTime <= 0)
        {
            Process p = wq[i];
            for (int j = i; j < wq_count - 1; j++)
            {
                wq[j] = wq[j + 1];
            }
            wq_count--;
            p.state = READY;
            add_to_dq(p);
        }
        else
        {
            i++;
        }
    }
    pthread_mutex_unlock(&lock) ;
}

void* monitor_process(void* arg)
{
    while (running)
    {
        wake_up_processes();
        printf("Monitor: Checking queues\n");
        print_queues();
        sleep(3); // X초마다 상태 출력 (예시로 3초 설정)
    }
    return NULL;
}

void schedule_processes()
{
    pthread_mutex_lock(&lock) ;

    // DQ에 있는 프로세스 실행
    while (dq_count > 0)
    {
        Process p = dq[0];
        for (int i = 0; i < dq_count - 1; i++)
        {
            dq[i] = dq[i + 1];
        }
        dq_count--;

        // 프로세스 실행
        printf("Executing process %d%c%s\n", p.pid, p.type, p.promoted == '*' ? "*" : "");
        p.state = RUNNING;
        sleep(1); // 프로세스 실행 시간 (1초)
        p.remainingTime--;

        // 프로세스 상태 업데이트
        if (p.remainingTime > 0)
        {
            // 프로세스 프로모션 확인
            if (p.type == 'B' && p.remainingTime <= 3)
            {
                p.promoted = '*';
                printf("Process %d%c%s promoted\n", p.pid, p.type, p.promoted);
            }
            p.state = BLOCKED;
            add_to_wq(p);
        }
        else
        {
            printf("Process %d%c%s finished\n", p.pid, p.type, p.promoted == '*' ? "*" : "");
            free(p.pid); // 프로세스 메모리 해제
        }
    }

    pthread_mutex_unlock(&lock) ;
}

char** parse(const char* command)
{
    char** tokens = malloc(100 * sizeof(char*)); // 최대 100개의 토큰
    char* cmd_copy = strdup(command); // 원본 문자열을 복사하여 사용
    char* token;
    int i = 0;

    token = strtok(cmd_copy, " ");
    while (token != NULL)
    {
        tokens[i++] = strdup(token); // 토큰 복사하여 할당
        token = strtok(NULL, " ");
    }
    tokens[i] = NULL; // 마지막 토큰은 NULL로 설정

    free(cmd_copy); // 복사한 원본 문자열 해제
    return tokens;
}

void exec(char** args)
{
    pid_t pid = fork();
    if (pid == 0)
    {
        // 자식 프로세스에서 명령어 실행
        if (execvp(args[0], args) < 0)
        {
            perror("execvp failed");
            // execvp가 실패했을 때만 메모리 해제
            int i = 0;
            while (args[i])
            {
                free(args[i]);
                i++;
            }
            free(args);
            exit(1);
        }
    }
    else if (pid > 0)
    {
        // 부모 프로세스에서 자식 프로세스가 종료될 때까지 기다림
        int status;
        waitpid(pid, &status, 0);
        // args 메모리 해제
        int i = 0;
        while (args[i])
        {
            free(args[i]); // 각 토큰에 대한 메모리 해제
            i++;
        }
        free(args); // args 자체에 대한 메모리 해제
    }
    else
    {
        perror("fork failed");
    }
}
int main()
{
    pthread_t shell_tid, monitor_tid;

    // Shell과 Monitor 프로세스(thread로 구현) 생성
    pthread_create(&shell_tid, NULL, shell_process, NULL);
    pthread_create(&monitor_tid, NULL, monitor_process, NULL);

    sleep(20);
    running = 0;

    pthread_join(shell_tid, NULL);
    pthread_join(monitor_tid, NULL);

    pthread_mutex_destroy(&lock) ;

    return 0;
}